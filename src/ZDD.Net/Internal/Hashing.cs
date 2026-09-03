using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace ZDD.Net.Internal
{
    /// <summary>
    /// 64-bit hash functions for the open-addressing tables (the node unique table keyed on
    /// <c>(level, lo, hi)</c>, and the frontier level state tables). Lighter weight than
    /// <see cref="System.HashCode"/>, which pays for a per-process randomized seed that these
    /// hot paths don't need.
    /// </summary>
    internal static class Hashing
    {
        /// <summary>Odd 64-bit constant derived from the golden ratio (<c>floor(2^64 / phi)</c>); used for both mixing and Fibonacci hashing.</summary>
        private const ulong GoldenRatio64 = 0x9E3779B97F4A7C15UL;

        /// <summary>The first SplitMix64-finalizer multiplier, broadcast to every lane for the vectorized mix.</summary>
        private static readonly Vector256<ulong> MixMul1x256 = Vector256.Create(0xBF58476D1CE4E5B9UL);

        /// <summary>The second SplitMix64-finalizer multiplier, broadcast to every lane for the vectorized mix.</summary>
        private static readonly Vector256<ulong> MixMul2x256 = Vector256.Create(0x94D049BB133111EBUL);

        private static readonly Vector128<ulong> MixMul1x128 = Vector128.Create(0xBF58476D1CE4E5B9UL);
        private static readonly Vector128<ulong> MixMul2x128 = Vector128.Create(0x94D049BB133111EBUL);

        /// <summary>
        /// Escape hatch for tests and CI: with <c>ZDD_DISABLE_SIMD=1</c> in the environment,
        /// <see cref="Combine(ReadOnlySpan{byte})"/> always takes the scalar path, exactly as it would on
        /// hardware where <see cref="Vector128.IsHardwareAccelerated"/> and <see cref="Vector256.IsHardwareAccelerated"/>
        /// are both <see langword="false"/>. Read once: nothing in this process changes it afterwards.
        /// </summary>
        private static readonly bool SimdDisabledForTesting =
            Environment.GetEnvironmentVariable("ZDD_DISABLE_SIMD") == "1";

        /// <summary>Mixes a 64-bit value (the SplitMix64 finalizer) so a single input bit flips roughly half the output bits.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Mix64(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }

        /// <summary>Combines a unique-table key <c>(level, lo, hi)</c> into a single 64-bit hash.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Combine(int level, int lo, int hi)
        {
            ulong hash = GoldenRatio64;
            hash = Mix64(hash ^ (uint)level);
            hash = Mix64(hash ^ (uint)lo);
            hash = Mix64(hash ^ (uint)hi);
            return hash;
        }

        /// <summary>
        /// Mixes a single hash code, such as the one a frontier spec returns for a state, so that a
        /// poorly distributed user hash still spreads over the slots.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Combine(int value) => Mix64(GoldenRatio64 ^ (uint)value);

        /// <summary>
        /// Below this length, <c>bench/ZDD.Net.Benchmarks -- hashing-simd</c> (docs/benchmarks.md,
        /// M4-2) measured the vectorized path as flat or a net loss (median of 5 runs: 0.85x-1.03x at
        /// 64-128 bytes; a clear, repeatable 1.2x+ only shows up from 256 bytes on): the branch checks
        /// and the one-time <c>Vector128.Create</c>/<c>Vector256.Create</c> setup cost more than a
        /// handful of 16- or 32-byte chunks save. Skipping straight to the scalar loop below this
        /// length — rather than always trying the widest chunk the hardware supports — is itself part
        /// of what M4-2 measured, not a fallback for unsupported hardware (that fallback is
        /// <see cref="Vector256.IsHardwareAccelerated"/>/<see cref="Vector128.IsHardwareAccelerated"/> below).
        /// </summary>
        private const int MinVectorizedLength = 256;

        /// <summary>
        /// Combines a packed frontier state into a single 64-bit hash; equal byte sequences of equal
        /// length hash equally, which is all the state tables need. This value is never persisted or
        /// compared across processes, so its exact bit pattern is free to differ between the
        /// vectorized and scalar paths below.
        /// </summary>
        /// <param name="bytes">One packed state; every state of a level has the same length.</param>
        /// <remarks>
        /// Runs <c>Vector256</c>/<c>Vector128</c> lanes of the SplitMix64 finalizer over 32/16-byte
        /// chunks when the hardware accelerates them and the input is long enough to be worth it
        /// (<see cref="MinVectorizedLength"/>; docs/ROADMAP.md M4-2), then finishes the remainder with
        /// the original eight-bytes-at-a-time scalar loop, unchanged from before M4-2. Every load goes
        /// through <c>Unsafe.Add</c> over a single bounds-checked <see cref="ReadOnlySpan{T}"/>
        /// reference rather than repeated <c>Slice</c> calls; every loop is bounded by
        /// <paramref name="bytes"/>'s own length, so no load ever reaches outside it
        /// (<c>HashingTests.CombineOverBytesNeverReadsPastTheGivenLength</c> guards this with poisoned
        /// trailing bytes).
        /// </remarks>
        public static ulong Combine(ReadOnlySpan<byte> bytes)
        {
            ulong hash = GoldenRatio64;
            int length = bytes.Length;
            ref byte source = ref MemoryMarshal.GetReference(bytes);
            int i = 0;

            if (!SimdDisabledForTesting && length >= MinVectorizedLength && Vector256.IsHardwareAccelerated)
            {
                Vector256<ulong> acc = Vector256.Create(GoldenRatio64);

                for (; i + Vector256<byte>.Count <= length; i += Vector256<byte>.Count)
                {
                    Debug.Assert(i + Vector256<byte>.Count <= length, "A 32-byte load must stay inside the span.");
                    Vector256<ulong> chunk = Vector256.LoadUnsafe(ref Unsafe.Add(ref source, i)).AsUInt64();
                    acc = Mix256(acc ^ chunk);
                }

                hash = Mix64(hash ^ acc.GetElement(0));
                hash = Mix64(hash ^ acc.GetElement(1));
                hash = Mix64(hash ^ acc.GetElement(2));
                hash = Mix64(hash ^ acc.GetElement(3));
            }
            else if (!SimdDisabledForTesting && length >= MinVectorizedLength && Vector128.IsHardwareAccelerated)
            {
                Vector128<ulong> acc = Vector128.Create(GoldenRatio64);

                for (; i + Vector128<byte>.Count <= length; i += Vector128<byte>.Count)
                {
                    Debug.Assert(i + Vector128<byte>.Count <= length, "A 16-byte load must stay inside the span.");
                    Vector128<ulong> chunk = Vector128.LoadUnsafe(ref Unsafe.Add(ref source, i)).AsUInt64();
                    acc = Mix128(acc ^ chunk);
                }

                hash = Mix64(hash ^ acc.GetElement(0));
                hash = Mix64(hash ^ acc.GetElement(1));
            }

            for (; i + sizeof(ulong) <= length; i += sizeof(ulong))
            {
                Debug.Assert(i + sizeof(ulong) <= length, "An 8-byte load must stay inside the span.");
                ulong word = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref source, i));
                if (!BitConverter.IsLittleEndian)
                {
                    word = BinaryPrimitives.ReverseEndianness(word);
                }

                hash = Mix64(hash ^ word);
            }

            if (i < length)
            {
                ulong tail = 0;

                for (int shift = 0; i < length; i++, shift += 8)
                {
                    Debug.Assert(i < length, "A tail byte read must stay inside the span.");
                    tail |= (ulong)Unsafe.Add(ref source, i) << shift;
                }

                hash = Mix64(hash ^ tail);
            }

            return hash;
        }

        /// <summary>The SplitMix64 finalizer (<see cref="Mix64(ulong)"/>), applied to all four lanes of a <see cref="Vector256{T}"/> at once.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector256<ulong> Mix256(Vector256<ulong> value)
        {
            value ^= value >>> 30;
            value *= MixMul1x256;
            value ^= value >>> 27;
            value *= MixMul2x256;
            value ^= value >>> 31;
            return value;
        }

        /// <summary>The SplitMix64 finalizer (<see cref="Mix64(ulong)"/>), applied to both lanes of a <see cref="Vector128{T}"/> at once.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<ulong> Mix128(Vector128<ulong> value)
        {
            value ^= value >>> 30;
            value *= MixMul1x128;
            value ^= value >>> 27;
            value *= MixMul2x128;
            value ^= value >>> 31;
            return value;
        }

        /// <summary>
        /// Maps an already-mixed 64-bit hash to a slot index for a power-of-two-sized table, via
        /// Fibonacci hashing (faster than <c>%</c> and more stable when low bits are biased).
        /// </summary>
        /// <param name="hash">An already-mixed 64-bit hash value.</param>
        /// <param name="tableSize">The table size; must be a power of two (at least 1).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexFor(ulong hash, int tableSize)
        {
            ThrowHelper.ThrowIfNotPositivePowerOfTwo(tableSize, nameof(tableSize));

            int bits = BitOperations.TrailingZeroCount(tableSize);
            if (bits == 0)
            {
                // A single-slot table always resolves to index 0. Shifting a 64bit value by
                // 64 is undefined in general, and C#'s shift-count masking (mod 64) would
                // otherwise turn this into a no-op shift instead of the intended "all bits".
                return 0;
            }

            return (int)((hash * GoldenRatio64) >> (64 - bits));
        }

        /// <summary>
        /// Same as <see cref="IndexFor"/> but validates <paramref name="tableSize"/> only via a
        /// Debug assertion, for hot paths (unique table, operation cache) that already guarantee it.
        /// </summary>
        /// <param name="hash">An already-mixed 64-bit hash value.</param>
        /// <param name="tableSize">The table size; must be a power of two greater than one (unvalidated). Use <see cref="IndexFor"/> for size 1.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexForPowerOfTwo(ulong hash, int tableSize)
        {
            Debug.Assert(
                tableSize > 1 && BitOperations.IsPow2(tableSize),
                $"'{nameof(tableSize)}' must be a power of two greater than one, but was {tableSize}.");

            int bits = BitOperations.TrailingZeroCount(tableSize);
            return (int)((hash * GoldenRatio64) >> (64 - bits));
        }
    }
}

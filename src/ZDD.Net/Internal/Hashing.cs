using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ZDD.Net.Internal
{
    /// <summary>
    /// 64-bit hash functions for the node unique table (an open-addressing hash table keyed on
    /// <c>(level, lo, hi)</c>). Lighter weight than <see cref="System.HashCode"/>, which pays for
    /// a per-process randomized seed that this hot path doesn't need.
    /// </summary>
    internal static class Hashing
    {
        /// <summary>Odd 64-bit constant derived from the golden ratio (<c>floor(2^64 / phi)</c>); used for both mixing and Fibonacci hashing.</summary>
        private const ulong GoldenRatio64 = 0x9E3779B97F4A7C15UL;

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

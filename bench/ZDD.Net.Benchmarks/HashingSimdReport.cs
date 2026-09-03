using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using ZDD.Net.Internal;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// Before/after micro-benchmark for M4-2 (issue #45): times
    /// <see cref="Hashing.Combine(ReadOnlySpan{byte})"/> as it stands today (the <c>Vector256</c>/
    /// <c>Vector128</c>-accelerated version) against <see cref="ScalarCombine"/>, a frozen copy of the
    /// eight-bytes-at-a-time scalar loop it replaced, and separately times the state-table byte
    /// comparison (<see cref="MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>,
    /// as <c>ArrayLevelStateTable.GetOrAdd</c> uses it) against <see cref="HandRolledVectorEquals"/>, a
    /// hand-written <c>Vector256</c>/<c>Vector128</c> equivalent, to check whether M4-2 has anything to
    /// add there too. This isolates the one thing at a time — the PLAN.md §2 rule to measure a single
    /// change at a time rules out reusing the noisy end-to-end <see cref="FrontierBuildBenchmarks"/>
    /// suite for this, since a build's wall time also carries packing, GC, and the spec's own per-edge
    /// logic. <c>dotnet run -c Release -- hashing-simd</c> runs it.
    /// </summary>
    internal static class HashingSimdReport
    {
        /// <summary>
        /// Packed-state byte lengths spanning the frontier widths <c>docs/benchmarks.md</c>'s
        /// representative cases actually reach: from a handful of slots (small grids) up to the
        /// ~2,600-slot frontier of <c>Cardinality_5000Choose2400To2600</c>.
        /// </summary>
        private static readonly int[] Lengths = { 8, 20, 64, 128, 256, 512, 1024, 2600 };

        public static void Run()
        {
            var random = new Random(20260903);

            Console.WriteLine("Combine(ReadOnlySpan<byte>): scalar (pre-M4-2) vs vectorized (current)");
            Console.WriteLine($"{"Bytes",8} {"Scalar (ns/op, median of 5)",28} {"SIMD (ns/op, median of 5)",26} {"Speedup",9}");

            foreach (int length in Lengths)
            {
                byte[] bytes = new byte[length];
                random.NextBytes(bytes);

                // Enough iterations for ~100-200ms of work per trial even at the largest length, so
                // trial-to-trial noise from the shared virtual environment stays small relative to
                // the measurement itself (docs/benchmarks.md's measurement-environment note).
                int iterationsForLength = Math.Max(200_000, 200_000_000 / Math.Max(1, length));

                double scalarNs = MedianOfFive(iterationsForLength, bytes, static (ReadOnlySpan<byte> b) => ScalarCombine(b));
                double simdNs = MedianOfFive(iterationsForLength, bytes, static (ReadOnlySpan<byte> b) => Hashing.Combine(b));

                Console.WriteLine($"{length,8} {scalarNs,24:F2}ns {simdNs,22:F2}ns {scalarNs / simdNs,8:F2}x");
            }

            Console.WriteLine();
            Console.WriteLine("State byte comparison: BCL SequenceEqual vs a hand-rolled Vector256/Vector128 version");
            Console.WriteLine($"{"Bytes",8} {"SequenceEqual (ns/op)",24} {"Hand-rolled (ns/op)",22} {"Speedup",9}");

            foreach (int length in Lengths)
            {
                byte[] left = new byte[length];
                byte[] right = new byte[length];
                random.NextBytes(left);
                left.CopyTo(right, 0);

                int iterationsForLength = Math.Max(200_000, 200_000_000 / Math.Max(1, length));

                double bclNs = MedianOfFiveEquals(iterationsForLength, left, right, static (a, b) => a.SequenceEqual(b));
                double handRolledNs = MedianOfFiveEquals(iterationsForLength, left, right, static (a, b) => HandRolledVectorEquals(a, b));

                Console.WriteLine($"{length,8} {bclNs,22:F2}ns {handRolledNs,20:F2}ns {bclNs / handRolledNs,8:F2}x");
            }
        }

        private static double MedianOfFive(int iterations, byte[] bytes, Func<ReadOnlySpan<byte>, ulong> combine)
        {
            Span<double> trials = stackalloc double[5];
            for (int trial = 0; trial < trials.Length; trial++)
            {
                trials[trial] = TimeNsPerOp(iterations, bytes, combine);
            }

            trials.Sort();
            return trials[trials.Length / 2];
        }

        private static double TimeNsPerOp(int iterations, byte[] bytes, Func<ReadOnlySpan<byte>, ulong> combine)
        {
            // Warm up the tier-1/tier-0 JIT and touch every page of `bytes` before timing.
            ulong sink = 0;
            for (int i = 0; i < Math.Min(iterations, 50_000); i++)
            {
                sink ^= combine(bytes);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                sink ^= combine(bytes);
            }

            stopwatch.Stop();

            // Prevent the loop above from being optimized away as dead code.
            if (sink == ulong.MaxValue && iterations == int.MinValue)
            {
                Console.WriteLine("unreachable");
            }

            return stopwatch.Elapsed.TotalNanoseconds / iterations;
        }

        private static double MedianOfFiveEquals(int iterations, byte[] left, byte[] right, Func<ReadOnlySpan<byte>, ReadOnlySpan<byte>, bool> equals)
        {
            Span<double> trials = stackalloc double[5];
            for (int trial = 0; trial < trials.Length; trial++)
            {
                trials[trial] = TimeNsPerOpEquals(iterations, left, right, equals);
            }

            trials.Sort();
            return trials[trials.Length / 2];
        }

        private static double TimeNsPerOpEquals(int iterations, byte[] left, byte[] right, Func<ReadOnlySpan<byte>, ReadOnlySpan<byte>, bool> equals)
        {
            bool sink = false;
            for (int i = 0; i < Math.Min(iterations, 50_000); i++)
            {
                sink ^= equals(left, right);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                sink ^= equals(left, right);
            }

            stopwatch.Stop();

            if (sink && iterations == int.MinValue)
            {
                Console.WriteLine("unreachable");
            }

            return stopwatch.Elapsed.TotalNanoseconds / iterations;
        }

        /// <summary>
        /// A hand-written candidate for the byte comparison <c>ArrayLevelStateTable.GetOrAdd</c> does
        /// via <see cref="MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/> today —
        /// written to check whether M4-2 has anything to add on top of the BCL's already-vectorized
        /// implementation. Not used anywhere outside this report.
        /// </summary>
        private static bool HandRolledVectorEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            int length = a.Length;
            ref byte ra = ref MemoryMarshal.GetReference(a);
            ref byte rb = ref MemoryMarshal.GetReference(b);
            int i = 0;

            if (Vector256.IsHardwareAccelerated && length >= Vector256<byte>.Count)
            {
                for (; i + Vector256<byte>.Count <= length; i += Vector256<byte>.Count)
                {
                    Vector256<byte> va = Vector256.LoadUnsafe(ref Unsafe.Add(ref ra, i));
                    Vector256<byte> vb = Vector256.LoadUnsafe(ref Unsafe.Add(ref rb, i));
                    if (va != vb)
                    {
                        return false;
                    }
                }
            }
            else if (Vector128.IsHardwareAccelerated && length >= Vector128<byte>.Count)
            {
                for (; i + Vector128<byte>.Count <= length; i += Vector128<byte>.Count)
                {
                    Vector128<byte> va = Vector128.LoadUnsafe(ref Unsafe.Add(ref ra, i));
                    Vector128<byte> vb = Vector128.LoadUnsafe(ref Unsafe.Add(ref rb, i));
                    if (va != vb)
                    {
                        return false;
                    }
                }
            }

            for (; i < length; i++)
            {
                if (Unsafe.Add(ref ra, i) != Unsafe.Add(ref rb, i))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// A frozen copy of <c>Hashing.Combine(ReadOnlySpan&lt;byte&gt;)</c> as it stood before M4-2:
        /// the eight-bytes-at-a-time scalar loop with no vectorization, kept here only as this report's
        /// "before" baseline.
        /// </summary>
        private static ulong ScalarCombine(ReadOnlySpan<byte> bytes)
        {
            const ulong goldenRatio64 = 0x9E3779B97F4A7C15UL;
            ulong hash = goldenRatio64;
            int i = 0;

            for (; i + sizeof(ulong) <= bytes.Length; i += sizeof(ulong))
            {
                hash = Hashing.Mix64(hash ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i, sizeof(ulong))));
            }

            if (i < bytes.Length)
            {
                ulong tail = 0;

                for (int shift = 0; i < bytes.Length; i++, shift += 8)
                {
                    tail |= (ulong)bytes[i] << shift;
                }

                hash = Hashing.Mix64(hash ^ tail);
            }

            return hash;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using ZDD.Net.Core;
using ZDD.Net.Frontier;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// Times every case of <see cref="MemoryReport"/> by repeating the build and keeping the fastest
    /// run. <c>dotnet run -c Release -- time</c> runs this; docs/benchmarks.md's M3-2 timing table is
    /// its output before and after the change.
    /// </summary>
    /// <remarks>
    /// The BenchmarkDotNet suite is the reference for absolute timings, but its three iterations leave
    /// the millisecond cases with a standard deviation of the same order as the numbers on a shared
    /// virtual machine. The minimum over many runs is the noise-resistant statistic — noise only ever
    /// adds time — which is what comparing two implementations needs.
    /// </remarks>
    internal static class BuildTimeReport
    {
        /// <summary>Runs of each case; a case slower than this many milliseconds gets fewer.</summary>
        private const int Runs = 30;
        private const double LongCaseMilliseconds = 100;
        private const int LongCaseRuns = 3;

        /// <summary>Times every case, or only those whose name contains <paramref name="filter"/>.</summary>
        /// <param name="filter">Part of a case name, or null for all of them.</param>
        public static void Run(string? filter = null)
        {
            Console.WriteLine($"{"Case",-40} {"Min",11} {"Median",11} {"Runs",5}");

            foreach ((string name, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) in MemoryReport.AllCases())
            {
                if (filter is null || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    Measure(name, variableCount, build);
                }
            }
        }

        private static void Measure(string name, int variableCount, Func<ZddManager, BuildOptions?, Zdd> build)
        {
            // Two warmups, and the faster of them decides the run count: the first build of a case
            // also pays for JIT and for filling the pools, which would otherwise put a case near the
            // threshold into a different bucket than the same case built by another library version.
            double warmup = Math.Min(TimeOne(variableCount, build), TimeOne(variableCount, build));
            int runs = warmup > LongCaseMilliseconds ? LongCaseRuns : Runs;
            List<double> times = new List<double>(runs);

            for (int i = 0; i < runs; i++)
            {
                times.Add(TimeOne(variableCount, build));
            }

            times.Sort();
            Console.WriteLine($"{name,-40} {times[0],9:F2}ms {times[times.Count / 2],9:F2}ms {runs,5}");
        }

        private static double TimeOne(int variableCount, Func<ZddManager, BuildOptions?, Zdd> build)
        {
            using ZddManager manager = new ZddManager(variableCount);
            Stopwatch stopwatch = Stopwatch.StartNew();
            Zdd result = build(manager, null);
            stopwatch.Stop();

            GC.KeepAlive(result);
            return stopwatch.Elapsed.TotalMilliseconds;
        }
    }
}

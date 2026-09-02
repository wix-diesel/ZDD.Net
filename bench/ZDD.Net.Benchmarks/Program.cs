using System;
using System.Linq;
using BenchmarkDotNet.Running;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// Entry point: <c>dotnet run -c Release --project bench/ZDD.Net.Benchmarks</c> runs the timed
    /// BenchmarkDotNet suite (issue #31's acceptance condition); passing <c>stats</c> instead runs
    /// <see cref="StatsReport"/>, which is how docs/benchmarks.md's peak-frontier-width and
    /// final-node-count columns were produced, and <c>edge-order</c> runs
    /// <see cref="EdgeOrderReport"/>, the before/after comparison of edge-order optimization (issue #33).
    /// </summary>
    internal static class Program
    {
        private static void Main(string[] args)
        {
            if (args.Any(a => string.Equals(a, "stats", StringComparison.OrdinalIgnoreCase)))
            {
                StatsReport.Run();
                return;
            }

            if (args.Any(a => string.Equals(a, "edge-order", StringComparison.OrdinalIgnoreCase)))
            {
                EdgeOrderReport.Run();
                return;
            }

            BenchmarkRunner.Run<FrontierBuildBenchmarks>();
        }
    }
}

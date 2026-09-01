using System;
using System.Linq;
using BenchmarkDotNet.Running;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// Entry point: <c>dotnet run -c Release --project bench/ZDD.Net.Benchmarks</c> runs the timed
    /// BenchmarkDotNet suite (issue #31's acceptance condition); passing <c>stats</c> instead runs
    /// <see cref="StatsReport"/>, which is how docs/benchmarks.md's peak-frontier-width and
    /// final-node-count columns were produced.
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

            BenchmarkRunner.Run<FrontierBuildBenchmarks>();
        }
    }
}

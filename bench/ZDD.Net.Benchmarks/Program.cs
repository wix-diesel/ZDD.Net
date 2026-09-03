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
    /// <see cref="EdgeOrderReport"/>, the before/after comparison of edge-order optimization (issue #33),
    /// <c>memory</c> / <c>time</c> run <see cref="MemoryReport"/> / <see cref="BuildTimeReport"/>,
    /// the peak-memory and build-time reports state bit-packing is measured against (issue #34), and
    /// <c>spec-composition</c> runs <see cref="SpecCompositionReport"/>, the direct-construction-vs-
    /// post-filter comparison for composed specs (issue #37), and <c>real-graph</c> runs
    /// <see cref="RealGraphReport"/>, the thousands-of-edges real-graph path counting record
    /// docs/benchmarks.md's M3-11 section is built from (issue #43).
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

            if (args.Any(a => string.Equals(a, "spec-composition", StringComparison.OrdinalIgnoreCase)))
            {
                SpecCompositionReport.Run();
                return;
            }

            if (args.Any(a => string.Equals(a, "real-graph", StringComparison.OrdinalIgnoreCase)))
            {
                RealGraphReport.Run();
                return;
            }

            int time = Array.FindIndex(args, a => string.Equals(a, "time", StringComparison.OrdinalIgnoreCase));
            if (time >= 0)
            {
                BuildTimeReport.Run(time + 1 < args.Length ? args[time + 1] : null);
                return;
            }

            int memory = Array.FindIndex(args, a => string.Equals(a, "memory", StringComparison.OrdinalIgnoreCase));
            if (memory >= 0)
            {
                // An optional case-name filter: one case per process keeps the pooled buffers of an
                // earlier case out of a later one's reading.
                MemoryReport.Run(memory + 1 < args.Length ? args[memory + 1] : null);
                return;
            }

            BenchmarkRunner.Run<FrontierBuildBenchmarks>();
        }
    }
}

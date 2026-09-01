using System;
using System.Collections.Generic;
using System.Diagnostics;
using ZDD.Net.Core;
using ZDD.Net.Frontier;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// Prints, for every case in <see cref="Cases"/>, the peak frontier width and final node count that
    /// BenchmarkDotNet's own report does not carry (it times <c>Build</c>, not what the primary
    /// <see cref="FrontierBuilder"/> call's <see cref="BuildOptions.Progress"/> saw along the way).
    /// <c>dotnet run -c Release -- stats</c> runs this instead of the timed benchmarks; docs/benchmarks.md's
    /// peak-frontier-width and final-node-count columns come from here.
    /// </summary>
    internal static class StatsReport
    {
        public static void Run()
        {
            Console.WriteLine($"{"Case",-40} {"Elapsed",10} {"Count",22} {"PeakWidth",10} {"FinalNodes",11}");

            foreach ((string name, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) in Cases.All)
            {
                List<BuildProgress> history = new List<BuildProgress>();
                BuildOptions options = new BuildOptions { Progress = new RecordingProgress(history) };

                using ZddManager manager = new ZddManager(variableCount);
                Stopwatch stopwatch = Stopwatch.StartNew();
                Zdd result = build(manager, options);
                stopwatch.Stop();

                int peakWidth = 0;
                foreach (BuildProgress report in history)
                {
                    peakWidth = Math.Max(peakWidth, report.FrontierSize);
                }

                // manager.NodeCount reads the node table in constant time (unlike Zdd.NodeCount, which
                // re-traverses the family). It is not quite "result's own node count" for the Union/Product
                // cases, whose primary build is only one operand — but no node is ever removed once
                // created (no GC yet), so it is the manager's full node footprint for the case, which is
                // what "final node count" is meant to convey here.
                Console.WriteLine(
                    $"{name,-40} {stopwatch.Elapsed.TotalMilliseconds,8:F1}ms {result.Count,22} " +
                    $"{peakWidth,10} {manager.NodeCount,11}");
            }
        }

        private sealed class RecordingProgress : IProgress<BuildProgress>
        {
            private readonly List<BuildProgress> _history;

            public RecordingProgress(List<BuildProgress> history)
            {
                _history = history;
            }

            public void Report(BuildProgress value) => _history.Add(value);
        }
    }
}

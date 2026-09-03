using System;
using System.Collections.Generic;
using System.Diagnostics;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// M3-5's own reason for existing (docs/PLAN.md &#167;6.3): direct composition
    /// (<c>spec1.And(spec2)</c>) must reach the same family as building each spec separately and then
    /// <see cref="Zdd.Intersect"/>/<see cref="Zdd.Union"/>-ing them, while never paying for the larger
    /// intermediate diagram post-filtering has to build first. <c>dotnet run -c Release -- spec-composition</c>
    /// runs it (issue #37); this is how docs/benchmarks.md's "M3-5" section was produced.
    /// </summary>
    internal static class SpecCompositionReport
    {
        public static void Run()
        {
            Console.WriteLine("=== Direct composition vs. post-filter (peak temporary nodes, PathSpec side) ===");
            Console.WriteLine(
                $"{"Case",-42} {"Approach",-12} {"PeakWidth",10} {"PeakNodes",10} {"FinalNodes",11} {"Elapsed",10} {"Count",16}");

            ReportPathAndCardinality("Path_Grid6x6_AnyEndpoints_And_AtMost4Edges", n: 6, maxEdges: 4);
            ReportPathAndCardinality("Path_Grid7x7_AnyEndpoints_And_AtMost6Edges", n: 7, maxEdges: 6);
        }

        private static void ReportPathAndCardinality(string caseName, int n, int maxEdges)
        {
            Graph grid = Graph.Grid(n, n);
            PathSpec anyPath = new PathSpec(grid, 0, 0, allowAnyEndpoints: true);
            CardinalitySpec shortEnough = new CardinalitySpec(grid.EdgeCount, 0, maxEdges);

            // Post-filter: PathSpec has to be built to completion — with every path in the graph, however
            // long — before Cardinality can even start narrowing it down. Its own peak is what post-filtering
            // pays that direct composition does not.
            using (ZddManager postFilterManager = new ZddManager(grid.EdgeCount))
            {
                List<BuildProgress> pathHistory = new List<BuildProgress>();
                Stopwatch pathStopwatch = Stopwatch.StartNew();
                Zdd paths = FrontierBuilder.Build<PathSpec>(
                    postFilterManager, anyPath, new BuildOptions { Progress = new RecordingProgress(pathHistory) });
                pathStopwatch.Stop();

                Zdd cardinalityOnly = FrontierBuilder.Build<CardinalitySpec, int>(postFilterManager, shortEnough);
                Zdd postFilterResult = paths.Intersect(cardinalityOnly);

                PrintRow(caseName, "PostFilter", pathHistory, postFilterManager.NodeCount, pathStopwatch, postFilterResult.Count);

                // Direct composition, same case, fresh manager so node counts aren't shared across approaches.
                using ZddManager directManager = new ZddManager(grid.EdgeCount);
                List<BuildProgress> directHistory = new List<BuildProgress>();
                Stopwatch directStopwatch = Stopwatch.StartNew();
                Zdd direct = FrontierBuilder.Build<
                    AndSpec<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>, PairState<int[], int>>(
                    directManager,
                    anyPath.AsSpec().And<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>(shortEnough),
                    new BuildOptions { Progress = new RecordingProgress(directHistory) });
                directStopwatch.Stop();

                PrintRow(caseName, "Direct", directHistory, directManager.NodeCount, directStopwatch, direct.Count);

                if (postFilterResult.Count != direct.Count)
                {
                    throw new InvalidOperationException(
                        $"{caseName}: post-filter count {postFilterResult.Count} != direct count {direct.Count}.");
                }
            }
        }

        private static void PrintRow(
            string name, string approach, List<BuildProgress> history, long finalNodes, Stopwatch stopwatch, System.Numerics.BigInteger count)
        {
            int peakWidth = 0;
            long peakNodes = 0;
            foreach (BuildProgress report in history)
            {
                peakWidth = Math.Max(peakWidth, report.FrontierSize);
                peakNodes = Math.Max(peakNodes, report.NodeCount);
            }

            Console.WriteLine(
                $"{name,-42} {approach,-12} {peakWidth,10} {peakNodes,10} {finalNodes,11} " +
                $"{stopwatch.Elapsed.TotalMilliseconds,8:F1}ms {count,16}");
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

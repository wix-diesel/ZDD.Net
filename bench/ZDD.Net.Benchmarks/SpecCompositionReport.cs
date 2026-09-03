using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// The direct-construction-vs-post-filter comparison docs/benchmarks.md's "M3-5 スペック合成"
    /// section is made of: building "simple path AND at most k edges" directly via
    /// <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/> against building the unconstrained path
    /// family first and <see cref="Zdd.Intersect"/>-ing the edge-count constraint in after the fact
    /// (issue #37). <c>dotnet run -c Release -- spec-composition</c> runs it.
    /// </summary>
    /// <remarks>
    /// The whole point of composing specs instead of composing already-built <see cref="Zdd"/>s is that
    /// the post-filter approach must fully expand and reduce the unconstrained family before the filter
    /// ever discards anything; the direct approach discards a branch the moment it can no longer satisfy
    /// both constraints, so its temporary-node peak tracks the (small) filtered result, not the (huge)
    /// unconstrained one. <see cref="BuildProgress.NodeCount"/> is cumulative over a build (temporary
    /// nodes are never freed mid-build), so the value from the last level reported is the build's total —
    /// which, since nothing is ever reduced away mid-build, is also its peak.
    /// </remarks>
    internal static class SpecCompositionReport
    {
        public static void Run()
        {
            Console.WriteLine("=== Direct AndSpec vs. post-filter Intersect: \"simple path AND <= k edges\" ===");
            Console.WriteLine(
                $"{"Case",-32} {"Approach",-12} {"PeakWidth",9} {"TempNodes",10} {"FinalNodes",10} {"Elapsed",10} {"Count",14}");

            foreach (Case c in Cases())
            {
                Report(c.Name, "Direct AND", () => BuildDirect(c));
                Report(c.Name, "Post-filter", () => BuildPostFilter(c));
                Console.WriteLine();
            }

            ReportCardinalityOverhead();
        }

        /// <summary>
        /// The "clean" composition case: two <see cref="IDdSpec{TState}"/> specs with a plain <c>int</c>
        /// state each, so <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/> pays no allocation and
        /// no extra frontier dimension beyond the two counters it already needs — unlike the
        /// <see cref="ArrayDdSpecAdapter{TSpec}"/> bridge the path case above needs. Shows the
        /// composition overhead in isolation, without a variable-length adapter's cost mixed in.
        /// </summary>
        private static void ReportCardinalityOverhead()
        {
            Console.WriteLine("=== Direct AndSpec vs. post-filter Intersect: two CardinalitySpecs (no array adapter) ===");
            Console.WriteLine($"{"Approach",-14} {"PeakWidth",9} {"TempNodes",10} {"FinalNodes",10} {"Elapsed",10} {"Count",22}");

            const int itemCount = 5000;
            CardinalitySpec specA = new CardinalitySpec(itemCount, 1000, 3500);
            CardinalitySpec specB = new CardinalitySpec(itemCount, 2000, 4500);

            using (ZddManager manager = new ZddManager(itemCount))
            {
                List<BuildProgress> history = new List<BuildProgress>();
                BuildOptions options = new BuildOptions { Progress = new RecordingProgress(history) };

                Stopwatch stopwatch = Stopwatch.StartNew();
                Zdd direct = FrontierBuilder.Build<
                    AndSpec<CardinalitySpec, int, CardinalitySpec, int>,
                    AndState<int, int>>(manager, specA.And<CardinalitySpec, int, CardinalitySpec, int>(specB), options);
                stopwatch.Stop();

                long tempNodes = history.Count == 0 ? 0 : history[^1].NodeCount;
                int peakWidth = history.Count == 0 ? 0 : history.Max(r => r.FrontierSize);
                Console.WriteLine(
                    $"{"Direct AND",-14} {peakWidth,9} {tempNodes,10} {manager.NodeCount,10} " +
                    $"{stopwatch.Elapsed.TotalMilliseconds,8:F1}ms {direct.Count,22}");
            }

            using (ZddManager manager = new ZddManager(itemCount))
            {
                List<BuildProgress> historyA = new List<BuildProgress>();
                List<BuildProgress> historyB = new List<BuildProgress>();

                Stopwatch stopwatch = Stopwatch.StartNew();
                Zdd a = FrontierBuilder.Build<CardinalitySpec, int>(
                    manager, specA, new BuildOptions { Progress = new RecordingProgress(historyA) });
                Zdd b = FrontierBuilder.Build<CardinalitySpec, int>(
                    manager, specB, new BuildOptions { Progress = new RecordingProgress(historyB) });
                Zdd postFiltered = a.Intersect(b);
                stopwatch.Stop();

                long tempNodes = (historyA.Count == 0 ? 0 : historyA[^1].NodeCount) + (historyB.Count == 0 ? 0 : historyB[^1].NodeCount);
                int peakWidth = Math.Max(
                    historyA.Count == 0 ? 0 : historyA.Max(r => r.FrontierSize),
                    historyB.Count == 0 ? 0 : historyB.Max(r => r.FrontierSize));
                Console.WriteLine(
                    $"{"Post-filter",-14} {peakWidth,9} {tempNodes,10} {manager.NodeCount,10} " +
                    $"{stopwatch.Elapsed.TotalMilliseconds,8:F1}ms {postFiltered.Count,22}");
            }
        }

        private readonly record struct Case(string Name, int GridSize, int MaxEdges);

        private static IEnumerable<Case> Cases()
        {
            // Grid(7,7): 84 edges, corner-to-corner Manhattan distance 12. Bounding at exactly the
            // shortest length keeps only the 924 = C(12,6) monotone lattice paths out of 575,780,564.
            yield return new Case("Path_Grid7x7_ExactlyShortest", 7, 12);
            yield return new Case("Path_Grid8x8_ExactlyShortest", 8, 14);
            yield return new Case("Path_Grid9x9_ExactlyShortest", 9, 16);
            yield return new Case("Path_Grid10x10_ExactlyShortest", 10, 18);
        }

        private static (long TempNodes, int PeakWidth, long FinalNodes, TimeSpan Elapsed, System.Numerics.BigInteger Count) BuildDirect(Case c)
        {
            Graph grid = Graph.Grid(c.GridSize, c.GridSize);
            int t = grid.VertexCount - 1;
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            PathSpec pathSpec = new PathSpec(grid, 0, t);
            CardinalitySpec atMostK = new CardinalitySpec(grid.EdgeCount, 0, c.MaxEdges);

            List<BuildProgress> history = new List<BuildProgress>();
            BuildOptions options = new BuildOptions { Progress = new RecordingProgress(history) };

            ArrayDdSpecAdapter<PathSpec> pathAsSpec = pathSpec.AsDdSpec();
            AndSpec<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int> composed =
                pathAsSpec.And<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>(atMostK);

            Stopwatch stopwatch = Stopwatch.StartNew();
            Zdd result = FrontierBuilder.Build<
                AndSpec<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>,
                AndState<int[], int>>(manager, composed, options);
            stopwatch.Stop();

            long tempNodes = history.Count == 0 ? 0 : history[^1].NodeCount;
            int peakWidth = history.Count == 0 ? 0 : history.Max(r => r.FrontierSize);
            return (tempNodes, peakWidth, manager.NodeCount, stopwatch.Elapsed, result.Count);
        }

        private static (long TempNodes, int PeakWidth, long FinalNodes, TimeSpan Elapsed, System.Numerics.BigInteger Count) BuildPostFilter(Case c)
        {
            Graph grid = Graph.Grid(c.GridSize, c.GridSize);
            int t = grid.VertexCount - 1;
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            List<BuildProgress> pathHistory = new List<BuildProgress>();
            BuildOptions pathOptions = new BuildOptions { Progress = new RecordingProgress(pathHistory) };

            Stopwatch stopwatch = Stopwatch.StartNew();
            Zdd paths = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, t), pathOptions);

            List<BuildProgress> cardinalityHistory = new List<BuildProgress>();
            BuildOptions cardinalityOptions = new BuildOptions { Progress = new RecordingProgress(cardinalityHistory) };
            Zdd atMostK = FrontierBuilder.Build<CardinalitySpec, int>(
                manager, new CardinalitySpec(grid.EdgeCount, 0, c.MaxEdges), cardinalityOptions);

            Zdd result = paths.Intersect(atMostK);
            stopwatch.Stop();

            long tempNodes =
                (pathHistory.Count == 0 ? 0 : pathHistory[^1].NodeCount) +
                (cardinalityHistory.Count == 0 ? 0 : cardinalityHistory[^1].NodeCount);
            int peakWidth = Math.Max(
                pathHistory.Count == 0 ? 0 : pathHistory.Max(r => r.FrontierSize),
                cardinalityHistory.Count == 0 ? 0 : cardinalityHistory.Max(r => r.FrontierSize));
            return (tempNodes, peakWidth, manager.NodeCount, stopwatch.Elapsed, result.Count);
        }

        private static void Report(
            string name,
            string approach,
            Func<(long TempNodes, int PeakWidth, long FinalNodes, TimeSpan Elapsed, System.Numerics.BigInteger Count)> build)
        {
            (long tempNodes, int peakWidth, long finalNodes, TimeSpan elapsed, System.Numerics.BigInteger count) = build();
            Console.WriteLine(
                $"{name,-32} {approach,-12} {peakWidth,9} {tempNodes,10} {finalNodes,10} " +
                $"{elapsed.TotalMilliseconds,8:F1}ms {count,14}");
        }

        private sealed class RecordingProgress : IProgress<BuildProgress>
        {
            private readonly List<BuildProgress> _history;

            public RecordingProgress(List<BuildProgress> history) => _history = history;

            public void Report(BuildProgress value) => _history.Add(value);
        }
    }
}

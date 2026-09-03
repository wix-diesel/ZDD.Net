using System;
using System.Diagnostics;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// Cache-tuning workloads for M4-1 (issue #44): unlike the other 10 representative cases (which
    /// each call exactly one cache-using operation), these chain many top-level <see cref="Zdd.Union"/>
    /// calls that share subproblems across calls — the only traffic pattern where the persistent,
    /// cross-call <see cref="OperationCache"/> (as opposed to <see cref="ZDD.Net.Core.OperationWorkspace"/>'s
    /// per-call memoization, which already dedupes everything within a single call) can do anything.
    /// <c>dotnet run -c Release -- cache-tuning</c> runs it.
    /// </summary>
    internal static class CacheTuningReport
    {
        public static void Run()
        {
            Console.WriteLine($"{"Case",-34} {"Elapsed",10} {"Lookups",12} {"Hits",12} {"HitRate",8} {"Overwrites",11} {"CacheCap",9} {"Nodes",10}");

            Report("CardinalityWindowChain_1000x21", () => CardinalityWindowChain(itemCount: 1000, windowWidth: 200, step: 40));
            Report("CardinalityWindowChain_3000x31", () => CardinalityWindowChain(itemCount: 3000, windowWidth: 400, step: 80));
            Report("PathLengthWindowChain_Grid8x8", () => PathLengthWindowChain(gridSize: 8));
        }

        private static void Report(string name, Func<(TimeSpan Elapsed, ZddStatistics Stats)> run)
        {
            (TimeSpan elapsed, ZddStatistics stats) = run();

            Console.WriteLine(
                $"{name,-34} {elapsed.TotalMilliseconds,8:F1}ms {stats.CacheLookups,12:N0} {stats.CacheHits,12:N0} " +
                $"{stats.CacheHitRate,8:P1} {stats.CacheOverwrites,11:N0} {stats.CacheCapacity,9:N0} {stats.NodeCount,10:N0}");
        }

        /// <summary>
        /// Unions a sliding window of <see cref="CardinalitySpec"/> families over the same item order.
        /// Consecutive windows overlap heavily and share the same variable ordering, so the subtrees
        /// away from the window boundary recur across separate top-level <see cref="Zdd.Union"/> calls —
        /// exactly the traffic the persistent <see cref="OperationCache"/> is meant to absorb.
        /// </summary>
        private static (TimeSpan, ZddStatistics) CardinalityWindowChain(int itemCount, int windowWidth, int step)
        {
            using ZddManager manager = new ZddManager(itemCount);
            Stopwatch stopwatch = Stopwatch.StartNew();

            Zdd? acc = null;
            for (int min = 0; min + windowWidth <= itemCount; min += step)
            {
                CardinalitySpec spec = new CardinalitySpec(itemCount, min, min + windowWidth);
                Zdd window = FrontierBuilder.Build<CardinalitySpec, int>(manager, spec);
                acc = acc is null ? window : acc.Value.Union(window);
            }

            stopwatch.Stop();
            return (stopwatch.Elapsed, manager.GetStatistics());
        }

        /// <summary>
        /// Unions a sliding window of "simple s-t path with edge count in [min, min+width)" families on
        /// a grid — the graph-benchmark counterpart of <see cref="CardinalityWindowChain"/>, built the
        /// same way M3-5's "direct AndSpec" case is (<c>PathSpec.And(CardinalitySpec)</c>). Consecutive
        /// length windows share most of the path tree away from the point where length is decided.
        /// </summary>
        private static (TimeSpan, ZddStatistics) PathLengthWindowChain(int gridSize)
        {
            Graph grid = Graph.Grid(gridSize, gridSize);
            int edgeCount = grid.EdgeCount;
            int shortest = 2 * (gridSize - 1);
            int windowWidth = Math.Max(2, shortest / 4);

            using ZddManager manager = new ZddManager(edgeCount);
            Stopwatch stopwatch = Stopwatch.StartNew();

            Zdd? acc = null;
            for (int min = shortest; min < edgeCount; min += windowWidth)
            {
                PathSpec pathSpec = new PathSpec(grid, 0, grid.VertexCount - 1);
                CardinalitySpec lengthSpec = new CardinalitySpec(edgeCount, min, min + windowWidth);

                AndSpec<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int> composed =
                    pathSpec.AsDdSpec().And<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>(lengthSpec);

                Zdd window = FrontierBuilder.Build<
                    AndSpec<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>,
                    AndState<int[], int>>(manager, composed);

                acc = acc is null ? window : acc.Value.Union(window);

                if (min + windowWidth >= shortest + windowWidth * 12)
                {
                    // Grid8x8 has 112 edges; stop after a representative number of windows so the
                    // case stays fast to run repeatedly while still chaining a couple dozen unions.
                    break;
                }
            }

            stopwatch.Stop();
            return (stopwatch.Elapsed, manager.GetStatistics());
        }
    }
}

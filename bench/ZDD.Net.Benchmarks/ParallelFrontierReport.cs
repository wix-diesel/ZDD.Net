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
    /// Sequential vs. parallel build time for M4-3 (issue #46): <c>dotnet run -c Release -- parallel-frontier</c>
    /// runs it. docs/benchmarks.md's M4-3 section is this report's output.
    /// </summary>
    /// <remarks>
    /// Only cases whose peak frontier width clears <c>TopDownExpander.MinPartitionWidth</c> /
    /// <c>ArrayTopDownExpander.MinPartitionWidth</c> (2048) ever run the parallel path at all — most of
    /// the existing 10 representative cases (docs/benchmarks.md's main table) stay well under that (peak
    /// widths in the tens to low thousands), so <c>LinearConstraint_1000ItemsKnapsack</c> and
    /// <c>Path_Grid3x9_Shuffled_AsGiven</c> (from the M3-2 section) are the only existing named cases wide
    /// enough to be representative here. The two <c>Synthetic_*</c> cases isolate why: they share the same
    /// wide, sustained frontier, differing only in whether <c>GetChild</c> is cheap or artificially
    /// expensive — see the "design decision" discussion in docs/benchmarks.md's M4-3 section.
    /// </remarks>
    internal static class ParallelFrontierReport
    {
        private const double LongCaseMilliseconds = 500;
        private const int Runs = 5;
        private const int LongCaseRuns = 3;

        public static void Run()
        {
            Console.WriteLine($"{"Case",-34} {"DOP=1 (Min)",12} {"DOP=N (Min)",12} {"Speedup (Min)",14} {"Speedup (round median)",23}");

            foreach ((string name, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) in WideCases())
            {
                Report(name, variableCount, build);
            }
        }

        private static IEnumerable<(string Name, Func<ZddManager, BuildOptions?, Zdd> Build, int VariableCount)> WideCases()
        {
            // Peak frontier width 12,751 (docs/benchmarks.md's main table) — the only one of the original
            // 10 representative cases wide enough to clear the partition threshold at all.
            yield return ("LinearConstraint_1000ItemsKnapsack", LookUp("LinearConstraint_1000ItemsKnapsack"), 1000);

            // Peak frontier width 457,728 (docs/benchmarks.md's M3-2 section) — by far the widest case
            // this project already has a name for, and the one this feature is squarely aimed at.
            Graph shuffledPath = EdgeOrderReport.Shuffle(Graph.Grid(3, 9), 7);
            yield return (
                "Path_Grid3x9_Shuffled_AsGiven",
                (manager, options) => FrontierBuilder.Build<PathSpec>(
                    manager, new PathSpec(shuffledPath, 0, shuffledPath.VertexCount - 1), options),
                shuffledPath.EdgeCount);

            // A wide, sustained frontier (17 levels at 200,000 states) with a trivially cheap GetChild
            // and a trivially cheap state (a single int) — isolates the state table's own GetOrAdd cost
            // from GetChild, since almost none of a state's cost here is GetChild.
            yield return (
                "Synthetic_WideCheapGetChild",
                (manager, options) => FrontierBuilder.Build<ScratchWideSpec, int>(manager, new ScratchWideSpec(35, 200_000), options),
                35);

            // Same shape, but GetChild does real (if artificial) work per call — isolates the opposite
            // case, where GetChild itself, not the state table, is what a build spends its time on.
            yield return (
                "Synthetic_WideExpensiveGetChild",
                (manager, options) => FrontierBuilder.Build<ScratchExpensiveWideSpec, int>(
                    manager, new ScratchExpensiveWideSpec(30, 20_000, 3000), options),
                30);
        }

        private static Func<ZddManager, BuildOptions?, Zdd> LookUp(string name)
        {
            foreach ((string caseName, Func<ZddManager, BuildOptions?, Zdd> build, int _) in Cases.All)
            {
                if (string.Equals(caseName, name, StringComparison.Ordinal))
                {
                    return build;
                }
            }

            throw new InvalidOperationException($"No such case in Cases.All: '{name}'.");
        }

        private static void Report(string name, int variableCount, Func<ZddManager, BuildOptions?, Zdd> build)
        {
            int degreeOfParallelism = Environment.ProcessorCount;
            BuildOptions sequentialOptions = new BuildOptions { MaxDegreeOfParallelism = 1 };
            BuildOptions parallelOptions = new BuildOptions { MaxDegreeOfParallelism = degreeOfParallelism };

            // One warmup round of each pays for JIT and pool fill-in before any round is timed.
            RunOnce(variableCount, build, sequentialOptions);
            RunOnce(variableCount, build, parallelOptions);

            double warmup = Math.Min(
                RunOnce(variableCount, build, sequentialOptions),
                RunOnce(variableCount, build, parallelOptions));
            int runs = warmup > LongCaseMilliseconds ? LongCaseRuns : Runs;

            List<double> sequentialTimes = new List<double>(runs);
            List<double> parallelTimes = new List<double>(runs);
            List<double> roundRatios = new List<double>(runs);

            // Alternate sequential/parallel each round rather than running all of one kind then the
            // other, so drift in the shared environment's load affects both sides equally
            // (docs/benchmarks.md's M3-2 timing methodology).
            for (int i = 0; i < runs; i++)
            {
                double sequential = RunOnce(variableCount, build, sequentialOptions);
                double parallel = RunOnce(variableCount, build, parallelOptions);

                sequentialTimes.Add(sequential);
                parallelTimes.Add(parallel);
                roundRatios.Add(sequential / parallel);
            }

            sequentialTimes.Sort();
            parallelTimes.Sort();
            roundRatios.Sort();

            double minSequential = sequentialTimes[0];
            double minParallel = parallelTimes[0];
            double medianRatio = roundRatios[roundRatios.Count / 2];

            Console.WriteLine(
                $"{name,-34} {minSequential,10:F1}ms {minParallel,10:F1}ms " +
                $"{minSequential / minParallel,12:F2}x {medianRatio,21:F2}x  (DOP={degreeOfParallelism}, runs={runs})");
        }

        private static double RunOnce(int variableCount, Func<ZddManager, BuildOptions?, Zdd> build, BuildOptions options)
        {
            using ZddManager manager = new ZddManager(variableCount);
            Stopwatch stopwatch = Stopwatch.StartNew();
            Zdd result = build(manager, options);
            stopwatch.Stop();

            GC.KeepAlive(result);
            return stopwatch.Elapsed.TotalMilliseconds;
        }
    }
}

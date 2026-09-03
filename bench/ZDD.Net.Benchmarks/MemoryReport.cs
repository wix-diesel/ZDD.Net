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
    /// Prints the peak live heap and the total allocation of every case, which is what state
    /// bit-packing (issue #34 / M3-2) has to move. <c>dotnet run -c Release -- memory</c> runs this;
    /// docs/benchmarks.md's M3-2 table is its output before and after the change.
    /// </summary>
    /// <remarks>
    /// Peak is sampled at level boundaries with a forced full collection, so pooled buffers that are
    /// merely rented still count (they are memory the process holds) while garbage does not. Those
    /// collections also make a build here far slower than it really is; <see cref="BuildTimeReport"/>
    /// is what times one.
    /// </remarks>
    internal static class MemoryReport
    {
        /// <summary>Roughly how many samples one build takes; each one forces a gen-2 collection.</summary>
        private const int SamplesPerBuild = 40;

        /// <summary>Measures every case, or only those whose name contains <paramref name="filter"/>.</summary>
        /// <param name="filter">
        /// Part of a case name, or null for all of them. One case per process is the honest way to read
        /// the numbers: <see cref="System.Buffers.ArrayPool{T}"/> keeps what an earlier case rented, and a
        /// later case that reuses those buffers looks free.
        /// </param>
        public static void Run(string? filter = null)
        {
            Console.WriteLine($"{"Case",-40} {"PeakLive",13} {"Allocated",13} {"PeakWidth",10} {"Nodes",10}");

            foreach ((string name, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) in AllCases())
            {
                if (filter is null || name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    Measure(name, variableCount, build);
                }
            }
        }

        /// <summary>
        /// The documented ten, plus cases whose frontier states rather than temporary nodes dominate:
        /// an edge order bad enough to make one level hold hundreds of thousands of states (the M3-1
        /// table's AsGiven rows). That is the shape thousands of edges produce, and what packing is for.
        /// </summary>
        public static IEnumerable<(string Name, Func<ZddManager, BuildOptions?, Zdd> Build, int VariableCount)> AllCases()
        {
            foreach ((string name, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) in Cases.All)
            {
                yield return (name, build, variableCount);
            }

            foreach ((string name, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) in WideFrontierCases())
            {
                yield return (name, build, variableCount);
            }

            foreach ((string name, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) in ComparisonReport.Cases())
            {
                yield return (name, build, variableCount);
            }
        }

        /// <summary>Builds one case, reporting what it held at its widest.</summary>
        private static void Measure(string name, int variableCount, Func<ZddManager, BuildOptions?, Zdd> build)
        {
            long baseline = Collect();
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);

            PeakSampler sampler = new PeakSampler(baseline);
            BuildOptions options = new BuildOptions { Progress = sampler };

            using ZddManager manager = new ZddManager(variableCount);
            Stopwatch stopwatch = Stopwatch.StartNew();
            Zdd result = build(manager, options);
            stopwatch.Stop();

            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            Console.WriteLine(
                $"{name,-40} {Bytes(sampler.PeakBytes),13} {Bytes(allocated),13} " +
                $"{sampler.PeakWidth,10} {manager.NodeCount,10}");

            GC.KeepAlive(result);
        }

        private static IEnumerable<(string Name, Func<ZddManager, BuildOptions?, Zdd> Build, int VariableCount)> WideFrontierCases()
        {
            Graph path = EdgeOrderReport.Shuffle(Graph.Grid(3, 9), 7);
            yield return (
                "Path_Grid3x9_Shuffled_AsGiven",
                (manager, options) => FrontierBuilder.Build<PathSpec>(manager, new PathSpec(path, 0, path.VertexCount - 1), options),
                path.EdgeCount);

            Graph tree = EdgeOrderReport.Shuffle(Graph.Grid(4, 5), 7);
            yield return (
                "SpanningTree_Grid4x5_Shuffled_AsGiven",
                (manager, options) => FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(tree), options),
                tree.EdgeCount);

            Graph forest = EdgeOrderReport.Shuffle(Graph.Grid(4, 5), 11);
            yield return (
                "Forest_Grid4x5_Shuffled_AsGiven",
                (manager, options) => FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(forest, 2), options),
                forest.EdgeCount);
        }

        /// <summary>Collects everything collectable and returns the live heap that is left.</summary>
        private static long Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return GC.GetTotalMemory(forceFullCollection: true);
        }

        private static string Bytes(long bytes) => $"{bytes / 1024.0:N1} KB";

        /// <summary>Samples the live heap every few levels and keeps the largest reading.</summary>
        private sealed class PeakSampler : IProgress<BuildProgress>
        {
            private readonly long _baseline;
            private int _interval;
            private int _reports;

            public PeakSampler(long baseline)
            {
                _baseline = baseline;
            }

            public long PeakBytes { get; private set; }

            public int PeakWidth { get; private set; }

            public void Report(BuildProgress value)
            {
                PeakWidth = Math.Max(PeakWidth, value.FrontierSize);

                if (_interval == 0)
                {
                    _interval = Math.Max(1, value.RootLevel / SamplesPerBuild);
                }

                if (_reports++ % _interval != 0)
                {
                    return;
                }

                PeakBytes = Math.Max(PeakBytes, GC.GetTotalMemory(forceFullCollection: true) - _baseline);
            }
        }
    }
}

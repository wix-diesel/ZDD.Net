using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// The record docs/benchmarks.md's M7 section is built from: how much wider/slower the frontier method
    /// gets once arcs replace edges (issue #159 / M7-8, docs/design/m7-directed-graphs.md §4). No performance
    /// target is set here &#8212; v0.7's goal is the directed *feature*, and doubling the variable count is
    /// expected to cost something; the point is measuring and recording how much, not meeting a bar.
    /// <c>dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- directed-graph</c> runs it.
    /// </summary>
    /// <remarks>
    /// Four sections, matching the design doc's table: (1) <see cref="DirectedGraph.Bidirected"/> grids'
    /// s&#8211;t paths against their undirected counterpart (also a correctness check &#8212; the two counts
    /// must match exactly, docs/design/m7-directed-graphs.md §3.2); (2) grids with a fraction of edges made
    /// one-way, tracking peak frontier width as that fraction grows; (3) <see cref="DirectedGraph.Complete"/>
    /// directed Hamiltonian cycles against <c>(n-1)!</c> and twice the undirected count; (4) arborescences
    /// against the directed matrix-tree theorem, both on <see cref="DirectedGraph.Bidirected"/> grids (which
    /// also have an undirected spanning-tree count to compare against) and on random reachable graphs (which
    /// do not). Every section throws if its cross-check fails, rather than merely printing a mismatch
    /// &#8212; a benchmark whose "correctness check" can silently go red is not one.
    /// </remarks>
    internal static class DirectedGraphReport
    {
        public static void Run()
        {
            BidirectedGridPaths();
            Console.WriteLine();
            OneWayMixedGrid();
            Console.WriteLine();
            EdgeOrderStrategyComparison();
            Console.WriteLine();
            CompleteHamiltonianCycles();
            Console.WriteLine();
            Arborescences();
        }

        /// <summary>
        /// Table row 1: <c>Bidirected(Grid(n,n))</c> s&#8211;t directed paths vs. the same grid's undirected
        /// s&#8211;t paths (n = 5..8). The two counts must match exactly (an undirected simple path has exactly
        /// one orientation starting at the corner used here), which is also how this doubles as OEIS A007764
        /// verification &#8212; A007764 is what <c>PathSpecTests.CountMatchesOeisA007764ForDiagonalGridPaths</c>
        /// already checks the undirected side against.
        /// </summary>
        private static void BidirectedGridPaths()
        {
            Console.WriteLine("=== Bidirected(Grid(n,n)) s–t paths: directed vs undirected (design doc §4, row 1) ===");
            Console.WriteLine(
                $"{"n",3} {"UndirMs",9} {"DirMs",9} {"TimeX",6} {"UndirW",7} {"DirW",6} {"WidthX",7} " +
                $"{"UndirNodes",10} {"DirNodes",9} {"NodesX",7} {"UndirMem",9} {"DirMem",9} {"MemX",6} {"Match",5}");

            for (int n = 5; n <= 8; n++)
            {
                Graph grid = Graph.Grid(n, n);
                BuildStats undirected = Measure(grid.EdgeCount, (manager, options) =>
                    FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, grid.VertexCount - 1), options));

                DirectedGraph directedGrid = DirectedGraph.Grid(n, n);
                BuildStats directed = Measure(directedGrid.EdgeCount, (manager, options) =>
                    FrontierBuilder.Build<DirectedPathSpec>(
                        manager, new DirectedPathSpec(directedGrid, 0, directedGrid.VertexCount - 1), options));

                bool match = undirected.Count == directed.Count;
                if (!match)
                {
                    throw new InvalidOperationException(
                        $"Grid {n}x{n}: directed path count {directed.Count} != undirected path count {undirected.Count}.");
                }

                Console.WriteLine(
                    $"{n,3} {undirected.ElapsedMs,7:F1}ms {directed.ElapsedMs,7:F1}ms {Ratio(directed.ElapsedMs, undirected.ElapsedMs),6} " +
                    $"{undirected.PeakWidth,7} {directed.PeakWidth,6} {Ratio(directed.PeakWidth, undirected.PeakWidth),7} " +
                    $"{undirected.NodeCount,10} {directed.NodeCount,9} {Ratio(directed.NodeCount, undirected.NodeCount),7} " +
                    $"{Bytes(undirected.PeakBytes),9} {Bytes(directed.PeakBytes),9} {Ratio(directed.PeakBytes, undirected.PeakBytes),6} {match,5}");
            }
        }

        /// <summary>
        /// Table row 2: a grid with each edge independently made one-way with probability <paramref
        /// name="p"/> is not literally a road network, but stands in for one the way <see
        /// cref="RealGraphReport"/>'s nearest-neighbor construction does (no internet access to fetch a real
        /// dataset). The per-edge threshold is fixed across every <c>p</c> so that a larger <c>p</c>'s arc set
        /// is always a <em>subset</em> of a smaller one's &#8212; which makes "path count never increases as
        /// <c>p</c> grows" a real correctness check, not just a plausibility read.
        /// </summary>
        private static void OneWayMixedGrid()
        {
            Console.WriteLine("=== One-way-mixed grid: peak width vs one-way fraction p (design doc §4, row 2) ===");
            Console.WriteLine($"{"n",3} {"p",5} {"Arcs",5} {"Ms",8} {"PeakMem",9} {"Width",6} {"Nodes",7} {"Count (digits)",20}");

            foreach (int n in new[] { 6, 7 })
            {
                Graph grid = Graph.Grid(n, n);
                uint state = ((uint)n * 2654435761u) ^ 0x9E3779B9u;
                double NextDouble()
                {
                    state = (state * 1664525u) + 1013904223u;
                    return (state >> 8) / (double)(1u << 24);
                }

                var oneWayThreshold = new double[grid.EdgeCount];
                var keepForward = new bool[grid.EdgeCount];
                for (int i = 0; i < grid.EdgeCount; i++)
                {
                    oneWayThreshold[i] = NextDouble();
                    keepForward[i] = NextDouble() < 0.5;
                }

                BigInteger? previousCount = null;
                foreach (double p in new[] { 0.0, 0.25, 0.5 })
                {
                    var arcs = new List<DirectedEdge>(grid.EdgeCount * 2);
                    for (int i = 0; i < grid.EdgeCount; i++)
                    {
                        Edge edge = grid.GetEdge(i);
                        bool oneWay = oneWayThreshold[i] < p;
                        if (!oneWay || keepForward[i])
                        {
                            arcs.Add(new DirectedEdge(edge.U, edge.V));
                        }

                        if (!oneWay || !keepForward[i])
                        {
                            arcs.Add(new DirectedEdge(edge.V, edge.U));
                        }
                    }

                    var directed = new DirectedGraph(grid.VertexCount, arcs);
                    BuildStats stats = Measure(directed.EdgeCount, (manager, options) =>
                        FrontierBuilder.Build<DirectedPathSpec>(
                            manager, new DirectedPathSpec(directed, 0, directed.VertexCount - 1), options));

                    if (previousCount is BigInteger previous && stats.Count > previous)
                    {
                        throw new InvalidOperationException(
                            $"Grid {n}x{n} p={p}: path count {stats.Count} rose above the count at a smaller p " +
                            $"({previous}) — a larger p's arc set should be a subset of a smaller p's.");
                    }

                    previousCount = stats.Count;

                    string count = stats.Count.ToString();
                    string countSummary = count.Length <= 20 ? count : $"~10^{count.Length - 1} ({count.Length} digits)";
                    Console.WriteLine(
                        $"{n,3} {p,5:F2} {directed.EdgeCount,5} {stats.ElapsedMs,6:F1}ms {Bytes(stats.PeakBytes),9} " +
                        $"{stats.PeakWidth,6} {stats.NodeCount,7} {countSummary,20}");
                }
            }
        }

        /// <summary>
        /// The issue's "edge-order strategy differences" ask, applied to the hardest one-way-mixed case
        /// above (n=7, p=0.5): unlike the sections that build a whole ZDD, <see
        /// cref="DirectedGraph.EstimateMaxFrontierSize(EdgeOrderStrategy, EdgeOrderOptions)"/> is O(V+E), so
        /// this needs no <see cref="FrontierBuilder"/> call at all (the same trick <see
        /// cref="EdgeOrderReport.ReportWidths"/> uses for the undirected case).
        /// </summary>
        private static void EdgeOrderStrategyComparison()
        {
            Console.WriteLine("=== Edge-order strategy on the hardest one-way-mixed case (n=7, p=0.5): width only, no build ===");

            const int n = 7;
            const double p = 0.5;
            Graph grid = Graph.Grid(n, n);
            uint state = unchecked(((uint)n * 2654435761u) ^ 0x9E3779B9u);
            double NextDouble()
            {
                state = (state * 1664525u) + 1013904223u;
                return (state >> 8) / (double)(1u << 24);
            }

            var arcs = new List<DirectedEdge>(grid.EdgeCount * 2);
            for (int i = 0; i < grid.EdgeCount; i++)
            {
                Edge edge = grid.GetEdge(i);
                bool oneWay = NextDouble() < p;
                bool keepForward = NextDouble() < 0.5;
                if (!oneWay || keepForward)
                {
                    arcs.Add(new DirectedEdge(edge.U, edge.V));
                }

                if (!oneWay || !keepForward)
                {
                    arcs.Add(new DirectedEdge(edge.V, edge.U));
                }
            }

            var directed = new DirectedGraph(grid.VertexCount, arcs);
            Console.WriteLine(
                $"Arcs={directed.EdgeCount}  AsGiven={directed.EstimateMaxFrontierSize(EdgeOrderStrategy.AsGiven)}  " +
                $"Bfs={directed.EstimateMaxFrontierSize(EdgeOrderStrategy.Bfs)}  " +
                $"BeamSearchPathWidth={directed.EstimateMaxFrontierSize(EdgeOrderStrategy.BeamSearchPathWidth)}");
        }

        /// <summary>
        /// Table row 3: <c>K_n</c>'s directed Hamiltonian cycles (n = 6..9) against <c>(n-1)!</c> and against
        /// twice the undirected Hamiltonian cycle count of the same <c>K_n</c> (each undirected cycle has
        /// exactly two orientations, docs/design/m7-directed-graphs.md §3.3) &#8212; two independent
        /// cross-checks on the same number.
        /// </summary>
        private static void CompleteHamiltonianCycles()
        {
            Console.WriteLine("=== K_n directed Hamiltonian cycles: (n-1)! and 2x undirected (design doc §4, row 3) ===");
            Console.WriteLine(
                $"{"n",3} {"UndirMs",9} {"DirMs",9} {"TimeX",6} {"UndirW",7} {"DirW",6} {"WidthX",7} " +
                $"{"UndirMem",9} {"DirMem",9} {"MemX",6} {"DirCount",16} {"(n-1)!",16} {"Match",5}");

            for (int n = 6; n <= 9; n++)
            {
                Graph completeUndirected = Graph.Complete(n);
                BuildStats undirected = Measure(completeUndirected.EdgeCount, (manager, options) =>
                    FrontierBuilder.Build<HamiltonianCycleSpec>(manager, new HamiltonianCycleSpec(completeUndirected), options));

                DirectedGraph completeDirected = DirectedGraph.Complete(n);
                BuildStats directed = Measure(completeDirected.EdgeCount, (manager, options) =>
                    FrontierBuilder.Build<DirectedHamiltonianCycleSpec>(
                        manager, new DirectedHamiltonianCycleSpec(completeDirected), options));

                BigInteger factorial = Factorial(n - 1);
                bool match = directed.Count == factorial && directed.Count == undirected.Count * 2;
                if (!match)
                {
                    throw new InvalidOperationException(
                        $"K_{n}: directed Hamiltonian cycle count {directed.Count}, expected (n-1)!={factorial} " +
                        $"and 2x undirected ({undirected.Count * 2}).");
                }

                Console.WriteLine(
                    $"{n,3} {undirected.ElapsedMs,7:F1}ms {directed.ElapsedMs,7:F1}ms {Ratio(directed.ElapsedMs, undirected.ElapsedMs),6} " +
                    $"{undirected.PeakWidth,7} {directed.PeakWidth,6} {Ratio(directed.PeakWidth, undirected.PeakWidth),7} " +
                    $"{Bytes(undirected.PeakBytes),9} {Bytes(directed.PeakBytes),9} {Ratio(directed.PeakBytes, undirected.PeakBytes),6} " +
                    $"{directed.Count,16} {factorial,16} {match,5}");
            }
        }

        /// <summary>
        /// Table row 4: arborescences, in two parts. First, <see cref="DirectedGraph.Bidirected"/>-style grids at a fixed
        /// root against the same grid's undirected spanning-tree count (they match exactly &#8212; fixing a
        /// root orients each spanning tree exactly one way, docs/design/m7-directed-graphs.md §3.4), which
        /// gives a directed/undirected multiplier the way the other rows do. Second, random reachable
        /// directed graphs (no undirected structure to compare against) checked purely against <see
        /// cref="CountArborescences"/>, an independent reimplementation of the directed matrix-tree theorem
        /// (Tutte's) &#8212; kept local to this project rather than referencing
        /// <c>tests/ZDD.Net.Tests/Harness/Kirchhoff.cs</c>, which the benchmarks project does not (and should
        /// not) depend on, mirroring how <see cref="RealGraphReport"/> keeps its own independent graph
        /// generator instead of reusing <see cref="EdgeOrderReport"/>'s.
        /// </summary>
        private static void Arborescences()
        {
            Console.WriteLine("=== Arborescences: directed matrix-tree cross-check (design doc §4, row 4) ===");
            Console.WriteLine("-- Bidirected grids vs undirected spanning trees, root=0 --");
            Console.WriteLine(
                $"{"Grid",6} {"UndirMs",9} {"DirMs",9} {"TimeX",6} {"UndirW",7} {"DirW",6} {"WidthX",7} " +
                $"{"UndirMem",9} {"DirMem",9} {"MemX",6} {"Match",5}");

            foreach ((int rows, int cols) in new (int, int)[] { (2, 3), (3, 3), (3, 4), (4, 4) })
            {
                Graph gridUndirected = Graph.Grid(rows, cols);
                BuildStats undirected = Measure(gridUndirected.EdgeCount, (manager, options) =>
                    FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(gridUndirected), options));

                DirectedGraph gridDirected = DirectedGraph.Grid(rows, cols);
                BuildStats directed = Measure(gridDirected.EdgeCount, (manager, options) =>
                    FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(gridDirected, root: 0), options));

                bool match = undirected.Count == directed.Count;
                if (!match)
                {
                    throw new InvalidOperationException(
                        $"Grid {rows}x{cols}: arborescence count {directed.Count} != spanning tree count {undirected.Count}.");
                }

                Console.WriteLine(
                    $"{rows + "x" + cols,6} {undirected.ElapsedMs,7:F1}ms {directed.ElapsedMs,7:F1}ms " +
                    $"{Ratio(directed.ElapsedMs, undirected.ElapsedMs),6} {undirected.PeakWidth,7} {directed.PeakWidth,6} " +
                    $"{Ratio(directed.PeakWidth, undirected.PeakWidth),7} {Bytes(undirected.PeakBytes),9} " +
                    $"{Bytes(directed.PeakBytes),9} {Ratio(directed.PeakBytes, undirected.PeakBytes),6} {match,5}");
            }

            Console.WriteLine();
            Console.WriteLine("-- Random reachable directed graphs vs the directed matrix-tree theorem (no undirected analogue) --");
            Console.WriteLine($"{"n",3} {"Arcs",5} {"Ms",8} {"PeakMem",9} {"Width",6} {"Nodes",7} {"Count",14} {"MatrixTree",14} {"Match",5}");

            foreach ((int vertexCount, double extraArcProbability, int seed) in new (int, double, int)[]
                {
                    (8, 0.2, 1), (10, 0.15, 2), (12, 0.1, 3),
                })
            {
                DirectedGraph graph = RandomReachableDirectedGraph(vertexCount, extraArcProbability, seed);
                BuildStats stats = Measure(graph.EdgeCount, (manager, options) =>
                    FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0), options));

                BigInteger expected = CountArborescences(graph, root: 0);
                bool match = stats.Count == expected;
                if (!match)
                {
                    throw new InvalidOperationException(
                        $"Random graph n={vertexCount} seed={seed}: arborescence count {stats.Count} != matrix-tree {expected}.");
                }

                Console.WriteLine(
                    $"{vertexCount,3} {graph.EdgeCount,5} {stats.ElapsedMs,6:F1}ms {Bytes(stats.PeakBytes),9} {stats.PeakWidth,6} {stats.NodeCount,7} " +
                    $"{stats.Count,14} {expected,14} {match,5}");
            }
        }

        /// <summary>
        /// A directed graph in which every vertex is reachable from vertex 0: an "increasing" random
        /// backbone (each vertex <c>v &gt; 0</c> gets one arc in from a uniformly random earlier vertex,
        /// which alone guarantees reachability from 0) plus extra random arcs sprinkled in with probability
        /// <paramref name="extraArcProbability"/> to give the arborescence count more structure than a bare
        /// tree would. Independent of (and deliberately not sharing code with) the similarly-named test
        /// helper in <c>ArborescenceSpecTests</c>.
        /// </summary>
        private static DirectedGraph RandomReachableDirectedGraph(int vertexCount, double extraArcProbability, int seed)
        {
            uint state = ((uint)seed * 2654435761u) ^ 0x9E3779B9u;
            double NextDouble()
            {
                state = (state * 1664525u) + 1013904223u;
                return (state >> 8) / (double)(1u << 24);
            }

            var edges = new List<DirectedEdge>();
            var seen = new HashSet<(int From, int To)>();

            for (int v = 1; v < vertexCount; v++)
            {
                int parent = (int)(NextDouble() * v);
                edges.Add(new DirectedEdge(parent, v));
                seen.Add((parent, v));
            }

            for (int u = 0; u < vertexCount; u++)
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    if (u == v || seen.Contains((u, v)))
                    {
                        continue;
                    }

                    if (NextDouble() < extraArcProbability)
                    {
                        edges.Add(new DirectedEdge(u, v));
                        seen.Add((u, v));
                    }
                }
            }

            return new DirectedGraph(vertexCount, edges);
        }

        /// <summary>
        /// The number of out-arborescences of <paramref name="graph"/> rooted at <paramref name="root"/>,
        /// via the directed matrix-tree theorem (Tutte's): build the in-degree Laplacian <c>L = D_in -
        /// A^T</c>, delete <paramref name="root"/>'s row and column, and take the determinant of what is
        /// left. A local reimplementation of <c>Kirchhoff.CountArborescences</c>
        /// (tests/ZDD.Net.Tests/Harness/Kirchhoff.cs) &#8212; see <see cref="Arborescences"/>'s remarks for
        /// why this project does not simply reference that one.
        /// </summary>
        private static BigInteger CountArborescences(DirectedGraph graph, int root)
        {
            int n = graph.VertexCount;
            if (n == 1)
            {
                return BigInteger.One;
            }

            var laplacian = new BigInteger[n, n];
            foreach (DirectedEdge arc in graph.Edges)
            {
                laplacian[arc.To, arc.To] += 1;
                laplacian[arc.To, arc.From] -= 1;
            }

            var minor = new BigInteger[n - 1, n - 1];
            int mi = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == root)
                {
                    continue;
                }

                int mj = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == root)
                    {
                        continue;
                    }

                    minor[mi, mj] = laplacian[i, j];
                    mj++;
                }

                mi++;
            }

            return BigInteger.Abs(Determinant(minor));
        }

        /// <summary>The determinant of a square integer matrix via the Bareiss algorithm (fraction-free Gaussian elimination).</summary>
        private static BigInteger Determinant(BigInteger[,] matrix)
        {
            int n = matrix.GetLength(0);
            if (n == 0)
            {
                return BigInteger.One;
            }

            BigInteger[,] m = (BigInteger[,])matrix.Clone();
            BigInteger previousPivot = BigInteger.One;
            int sign = 1;

            for (int k = 0; k < n - 1; k++)
            {
                if (m[k, k] == BigInteger.Zero)
                {
                    int pivotRow = -1;
                    for (int i = k + 1; i < n; i++)
                    {
                        if (m[i, k] != BigInteger.Zero)
                        {
                            pivotRow = i;
                            break;
                        }
                    }

                    if (pivotRow < 0)
                    {
                        return BigInteger.Zero;
                    }

                    for (int j = 0; j < n; j++)
                    {
                        (m[k, j], m[pivotRow, j]) = (m[pivotRow, j], m[k, j]);
                    }

                    sign = -sign;
                }

                for (int i = k + 1; i < n; i++)
                {
                    for (int j = k + 1; j < n; j++)
                    {
                        m[i, j] = ((m[i, j] * m[k, k]) - (m[i, k] * m[k, j])) / previousPivot;
                    }
                }

                previousPivot = m[k, k];
            }

            return sign * m[n - 1, n - 1];
        }

        private static BigInteger Factorial(int n)
        {
            BigInteger result = BigInteger.One;
            for (int i = 2; i <= n; i++)
            {
                result *= i;
            }

            return result;
        }

        private static string Ratio(double numerator, double denominator) =>
            denominator == 0 ? "n/a" : $"{numerator / denominator:F2}x";

        private static BuildStats Measure(int variableCount, Func<ZddManager, BuildOptions, Zdd> build)
        {
            long baseline = Collect();
            var sampler = new PeakSampler(baseline);
            var options = new BuildOptions { Progress = sampler };

            using ZddManager manager = new ZddManager(variableCount);
            Stopwatch stopwatch = Stopwatch.StartNew();
            Zdd result = build(manager, options);
            stopwatch.Stop();

            return new BuildStats(stopwatch.Elapsed.TotalMilliseconds, sampler.PeakWidth, sampler.PeakBytes, manager.NodeCount, result.Count);
        }

        private static long Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return GC.GetTotalMemory(forceFullCollection: true);
        }

        private static string Bytes(long bytes) => $"{bytes / (1024.0 * 1024.0):N1}MB";

        private readonly struct BuildStats
        {
            public BuildStats(double elapsedMs, int peakWidth, long peakBytes, long nodeCount, BigInteger count)
            {
                ElapsedMs = elapsedMs;
                PeakWidth = peakWidth;
                PeakBytes = peakBytes;
                NodeCount = nodeCount;
                Count = count;
            }

            public double ElapsedMs { get; }

            public int PeakWidth { get; }

            public long PeakBytes { get; }

            public long NodeCount { get; }

            public BigInteger Count { get; }
        }

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
                    _interval = Math.Max(1, value.RootLevel / 40);
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

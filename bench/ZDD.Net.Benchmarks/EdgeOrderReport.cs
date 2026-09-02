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
    /// The before/after comparison docs/benchmarks.md's "M3-1 辺順序最適化" and "M3-3 ビームサーチ"
    /// sections are made of: what <see cref="Graph.Optimize"/> does to the peak frontier of a
    /// thousands-of-edges graph, and what that does to an actual build. <c>dotnet run -c Release -- edge-order</c>
    /// runs it (issues #33, #35).
    /// </summary>
    /// <remarks>
    /// The graphs here arrive in an arbitrary edge order, which is the realistic case — an edge list read
    /// from a file is written in whatever order its author chose, not in one that keeps a frontier narrow.
    /// A grid's own factory order is already a good order, so it would understate what reordering is for.
    /// </remarks>
    internal static class EdgeOrderReport
    {
        public static void Run()
        {
            ReportWidths();
            Console.WriteLine();
            ReportBuilds();
            Console.WriteLine();
            ReportBeamSearch();
            Console.WriteLine();
            ReportBeamWidthTrend();
        }

        private static void ReportWidths()
        {
            Console.WriteLine("=== Peak frontier width by strategy (no build) ===");
            Console.WriteLine($"{"Case",-26} {"Edges",6} {"AsGiven",8} {"Bfs",6} {"Dfs",6} {"Grid",6} {"Optimize(Bfs)",14}");

            foreach ((string name, Graph graph) in WidthCases())
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                Graph optimized = graph.Optimize(EdgeOrderStrategy.Bfs);
                stopwatch.Stop();

                Console.WriteLine(
                    $"{name,-26} {graph.EdgeCount,6} {graph.EstimateMaxFrontierSize(),8} " +
                    $"{optimized.EstimateMaxFrontierSize(),6} " +
                    $"{graph.EstimateMaxFrontierSize(EdgeOrderStrategy.Dfs),6} " +
                    $"{graph.EstimateMaxFrontierSize(EdgeOrderStrategy.Grid),6} " +
                    $"{stopwatch.Elapsed.TotalMilliseconds,12:F2}ms");
            }
        }

        private static void ReportBuilds()
        {
            Console.WriteLine("=== Same family, three edge orders (build) ===");
            Console.WriteLine($"{"Case",-30} {"Strategy",-8} {"Width",6} {"PeakStates",11} {"Nodes",9} {"Elapsed",11} {"Count",18}");

            foreach ((string name, Graph graph, Func<Graph, ZddManager, BuildOptions, Zdd> build) in BuildCases())
            {
                foreach (EdgeOrderStrategy strategy in new[]
                {
                    EdgeOrderStrategy.AsGiven, EdgeOrderStrategy.Bfs, EdgeOrderStrategy.Grid,
                })
                {
                    Graph ordered = graph.Optimize(strategy);
                    var history = new List<BuildProgress>();
                    var options = new BuildOptions { Progress = new RecordingProgress(history) };

                    using ZddManager manager = new ZddManager(ordered.EdgeCount);
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    Zdd result = build(ordered, manager, options);
                    stopwatch.Stop();

                    int peakStates = history.Count == 0 ? 0 : history.Max(report => report.FrontierSize);

                    // The Count column is the point of the whole table: reordering the edges must not change
                    // the family, only what it costs to build it.
                    Console.WriteLine(
                        $"{name,-30} {strategy,-8} {ordered.EstimateMaxFrontierSize(),6} {peakStates,11} " +
                        $"{manager.NodeCount,9} {stopwatch.Elapsed.TotalMilliseconds,9:F1}ms {result.Count,18}");
                }
            }
        }

        private static IEnumerable<(string Name, Graph Graph)> WidthCases()
        {
            yield return ("Grid40x40_Shuffled", Shuffle(Graph.Grid(40, 40), 7));
            yield return ("Grid30x60_Shuffled", Shuffle(Graph.Grid(30, 60), 11));
            yield return ("Torus30x30_Shuffled", Shuffle(Torus(30, 30), 3));
            yield return ("Random500v2000e", RandomGraph(500, 2000, 5));
        }

        /// <summary>
        /// The before/after comparison docs/benchmarks.md's "M3-3 ビームサーチ" section is made of:
        /// <see cref="EdgeOrderStrategy.BeamSearchPathWidth"/> against <see cref="EdgeOrderStrategy.Bfs"/>
        /// on the M3-1 irregular case plus a set of geometric graphs, standing in for the "thousands of
        /// edges, not a grid" real graphs the issue targets — roads and power grids are mostly local, not
        /// uniform-random, so <see cref="GeometricGraph"/>'s nearest-neighbor construction is closer to
        /// that than <c>RandomGraph</c>'s uniform edges are (issue #35).
        /// </summary>
        private static void ReportBeamSearch()
        {
            Console.WriteLine("=== BeamSearchPathWidth vs Bfs (no build) ===");
            Console.WriteLine($"{"Case",-24} {"Edges",6} {"Bfs",6} {"BeamSearch",10} {"Improvement",11} {"Preprocessing",13}");

            foreach ((string name, Graph graph) in BeamSearchCases())
            {
                int bfs = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.Bfs);

                Stopwatch stopwatch = Stopwatch.StartNew();
                int beam = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.BeamSearchPathWidth);
                stopwatch.Stop();

                double improvement = 100.0 * (bfs - beam) / bfs;
                Console.WriteLine(
                    $"{name,-24} {graph.EdgeCount,6} {bfs,6} {beam,10} {improvement,10:F0}% " +
                    $"{stopwatch.Elapsed.TotalMilliseconds,11:F0}ms");
            }
        }

        /// <summary>
        /// The completion criterion "widening the beam does not get worse" (issue #35), measured on two
        /// representative cases at beam widths 1 / 4 / 8 (the default) / 16.
        /// </summary>
        private static void ReportBeamWidthTrend()
        {
            Console.WriteLine("=== BeamSearchPathWidth by beam width ===");
            Console.WriteLine($"{"Case",-24} {"Edges",6} {"K=1",6} {"K=4",6} {"K=8",6} {"K=16",6}");

            foreach ((string name, Graph graph) in new[]
            {
                ("Random500v2000e", RandomGraph(500, 2000, 5)),
                ("Geo1000_k4", GeometricGraph(1000, 4, 3)),
            })
            {
                int k1 = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.BeamSearchPathWidth, EdgeOrderOptions.Default.WithBeamWidth(1));
                int k4 = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.BeamSearchPathWidth, EdgeOrderOptions.Default.WithBeamWidth(4));
                int k8 = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.BeamSearchPathWidth, EdgeOrderOptions.Default.WithBeamWidth(8));
                int k16 = graph.EstimateMaxFrontierSize(EdgeOrderStrategy.BeamSearchPathWidth, EdgeOrderOptions.Default.WithBeamWidth(16));

                Console.WriteLine($"{name,-24} {graph.EdgeCount,6} {k1,6} {k4,6} {k8,6} {k16,6}");
            }
        }

        private static IEnumerable<(string Name, Graph Graph)> BeamSearchCases()
        {
            yield return ("Random500v2000e", RandomGraph(500, 2000, 5));
            yield return ("Random300v900e", RandomGraph(300, 900, 5));
            yield return ("Random1000v4000e", RandomGraph(1000, 4000, 21));
            yield return ("Geo800_k4", GeometricGraph(800, 4, 2));
            yield return ("Geo1000_k4", GeometricGraph(1000, 4, 3));
            yield return ("Geo2000_k4", GeometricGraph(2000, 4, 4));
            yield return ("Grid40x40_Shuffled", Shuffle(Graph.Grid(40, 40), 7));
        }

        /// <summary>
        /// A "road/power network"-like graph: <paramref name="vertexCount"/> points placed uniformly at
        /// random in the unit square, each connected to its <paramref name="k"/> nearest neighbors. Unlike
        /// <see cref="RandomGraph"/>'s uniform-random edges, this has real local structure — closer to what
        /// M3-3 targets — while still being irregular, unlike <see cref="Graph.Grid"/>.
        /// </summary>
        private static Graph GeometricGraph(int vertexCount, int k, int seed)
        {
            uint state = (uint)seed + 0x9E3779B9u;
            double NextDouble()
            {
                state = (state * 1664525u) + 1013904223u;
                return (state >> 8) / (double)(1u << 24);
            }

            var x = new double[vertexCount];
            var y = new double[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                x[i] = NextDouble();
                y[i] = NextDouble();
            }

            var seen = new HashSet<Edge>();
            var edges = new List<Edge>();
            for (int i = 0; i < vertexCount; i++)
            {
                IEnumerable<int> nearest = Enumerable.Range(0, vertexCount)
                    .Where(j => j != i)
                    .OrderBy(j => ((x[i] - x[j]) * (x[i] - x[j])) + ((y[i] - y[j]) * (y[i] - y[j])))
                    .Take(k);

                foreach (int j in nearest)
                {
                    var edge = new Edge(Math.Min(i, j), Math.Max(i, j));
                    if (seen.Add(edge))
                    {
                        edges.Add(edge);
                    }
                }
            }

            return new Graph(vertexCount, edges);
        }

        private static IEnumerable<(string Name, Graph Graph, Func<Graph, ZddManager, BuildOptions, Zdd> Build)> BuildCases()
        {
            yield return (
                "Path_Grid3x9_Shuffled",
                Shuffle(Graph.Grid(3, 9), 7),
                (graph, manager, options) => FrontierBuilder.Build<PathSpec>(
                    manager, new PathSpec(graph, 0, graph.VertexCount - 1), options));

            yield return (
                "SpanningTree_Grid4x5_Shuffled",
                Shuffle(Graph.Grid(4, 5), 7),
                (graph, manager, options) => FrontierBuilder.Build<SpanningTreeSpec>(
                    manager, new SpanningTreeSpec(graph), options));

            yield return (
                "Path_Grid5x5_FactoryOrder",
                Graph.Grid(5, 5),
                (graph, manager, options) => FrontierBuilder.Build<PathSpec>(
                    manager, new PathSpec(graph, 0, graph.VertexCount - 1), options));
        }

        /// <summary>
        /// Rearranges the edges into an arbitrary order with a fixed linear congruential generator, so the
        /// report is reproducible on any runtime (unlike <see cref="Random"/>, whose algorithm is not
        /// contractual).
        /// </summary>
        public static Graph Shuffle(Graph graph, int seed)
        {
            int[] order = Enumerable.Range(0, graph.EdgeCount).ToArray();
            uint state = (uint)seed + 0x9E3779B9u;

            for (int i = order.Length - 1; i > 0; i--)
            {
                state = (state * 1664525u) + 1013904223u;
                int j = (int)(state % (uint)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }

            return graph.WithEdgeOrder(order);
        }

        private static Graph Torus(int rows, int cols)
        {
            var edges = new List<Edge>(2 * rows * cols);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int v = (r * cols) + c;
                    edges.Add(new Edge(v, (r * cols) + ((c + 1) % cols)));
                    edges.Add(new Edge(v, (((r + 1) % rows) * cols) + c));
                }
            }

            return new Graph(rows * cols, edges);
        }

        /// <summary>A connected pseudo-random graph: a spanning backbone plus extra edges, from a fixed generator.</summary>
        private static Graph RandomGraph(int vertexCount, int edgeCount, int seed)
        {
            var edges = new List<Edge>(edgeCount);
            var seen = new HashSet<Edge>();
            uint state = (uint)seed + 0x9E3779B9u;

            int Next(int bound)
            {
                state = (state * 1664525u) + 1013904223u;
                return (int)(state % (uint)bound);
            }

            for (int v = 1; v < vertexCount; v++)
            {
                var edge = new Edge(Next(v), v);
                if (seen.Add(edge))
                {
                    edges.Add(edge);
                }
            }

            while (edges.Count < edgeCount)
            {
                int u = Next(vertexCount);
                int v = Next(vertexCount);
                if (u == v)
                {
                    continue;
                }

                var edge = new Edge(u, v);
                if (seen.Add(edge))
                {
                    edges.Add(edge);
                }
            }

            return new Graph(vertexCount, edges);
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

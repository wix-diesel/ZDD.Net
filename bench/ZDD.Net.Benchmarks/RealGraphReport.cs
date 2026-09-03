using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Io;
using ZDD.Net.Specs;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// The record docs/benchmarks.md's "M3-11" section is made of: does s&#8211;t simple-path counting
    /// complete on a thousands-of-edges real graph (issue #43's central acceptance condition)? Builds a
    /// road-network-like graph, round-trips it through <see cref="DimacsGraph"/> the way a real data file
    /// would arrive, reorders it with <see cref="Graph.Optimize(EdgeOrderStrategy, EdgeOrderOptions)"/>,
    /// and reports whether <see cref="PathSpec"/> finishes &#8212; with real time, peak memory, and peak
    /// frontier width &#8212; at increasing scale until (if ever) it stops. <c>dotnet run -c Release --
    /// real-graph</c> runs it.
    /// </summary>
    /// <remarks>
    /// No internet access is available while producing this benchmark, so &#8220;real graph&#8221; here
    /// means the same nearest-neighbor construction <see cref="EdgeOrderReport"/> uses to stand in for a
    /// road or power network (issue #35's rationale): local structure, not uniform-random edges, is what
    /// makes graphs like this different from <see cref="Graph.Grid"/> or a uniform random graph. The
    /// honesty this section commits to is about the *result* (did it finish, what did it cost), not about
    /// having sourced the edges from an actual published dataset.
    /// </remarks>
    internal static class RealGraphReport
    {
        /// <summary>A node-count safety cap so a case that will not finish fails fast instead of exhausting memory.</summary>
        private const int MaxNodeCount = 30_000_000;

        public static void Run()
        {
            Console.WriteLine("=== Real-graph s–t path counting (DIMACS round-trip + Optimize) ===");
            Console.WriteLine(
                $"{"Case",-16} {"Vertices",8} {"Edges",7} {"AsGiven",8} {"Bfs",6} {"PeakWidth",9} " +
                $"{"PeakMem",12} {"Elapsed",10} {"Nodes",9} {"Count (digits)",30}");

            foreach ((string name, int vertexCount, int k, int seed) in Cases())
            {
                RunCase(name, vertexCount, k, seed);
            }
        }

        private static IEnumerable<(string Name, int VertexCount, int K, int Seed)> Cases()
        {
            // Sparse (k=2), near-tree road-network shape: stays narrow and completes from a thousand
            // edges up through tens of thousands.
            yield return ("Road_1000_k2", 1000, 2, 17);
            yield return ("Road_4000_k2", 4000, 2, 23);
            yield return ("Road_16000_k2", 16000, 2, 31);
            yield return ("Road_32000_k2", 32000, 2, 37);

            // Denser (k=4) road-network shape: BFS keeps the frontier *width* just as narrow (see the
            // Bfs column), but the number of alternate s–t routes still explodes the state count at
            // each level — the honest boundary docs/benchmarks.md's M3-11 section records.
            yield return ("Road_1000_k4", 1000, 4, 11);
            yield return ("Road_2000_k4", 2000, 4, 13);
            yield return ("Road_4000_k4", 4000, 4, 17);
        }

        private static void RunCase(string name, int vertexCount, int k, int seed)
        {
            // Build a road-network-like graph, then round-trip it through DIMACS text exactly as a user
            // loading a real data file would (docs/tutorial.md §3): the graph the build below actually
            // uses is the one that came back out of DimacsGraph.Read, not the in-memory one above.
            Graph source = RoadNetwork(vertexCount, k, seed);
            string dimacs = DimacsGraph.Write(source);
            Graph graph = DimacsGraph.Read(dimacs);

            (int s, int t) = FarthestPairInLargestComponent(graph);

            int asGiven = graph.EstimateMaxFrontierSize();
            Graph optimized = graph.Optimize(EdgeOrderStrategy.Bfs);
            int bfsWidth = optimized.EstimateMaxFrontierSize();

            long baseline = Collect();
            var sampler = new PeakSampler(baseline);
            var options = new BuildOptions { Progress = sampler, MaxNodeCount = MaxNodeCount };

            using ZddManager manager = new ZddManager(optimized.EdgeCount);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                // Optimize reorders edges only; vertex indices are untouched, so s/t apply to the
                // optimized graph unchanged.
                Zdd paths = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(optimized, s, t), options);
                stopwatch.Stop();

                string count = paths.Count.ToString();
                string countSummary = count.Length <= 24 ? count : $"~10^{count.Length - 1} ({count.Length} digits)";

                Console.WriteLine(
                    $"{name,-16} {graph.VertexCount,8} {graph.EdgeCount,7} {asGiven,8} {bfsWidth,6} " +
                    $"{sampler.PeakWidth,9} {Bytes(sampler.PeakBytes),12} {stopwatch.Elapsed.TotalMilliseconds,8:F0}ms " +
                    $"{manager.NodeCount,9} {countSummary,30}");

                GC.KeepAlive(paths);
            }
            catch (BuildLimitExceededException)
            {
                stopwatch.Stop();
                Console.WriteLine(
                    $"{name,-16} {graph.VertexCount,8} {graph.EdgeCount,7} {asGiven,8} {bfsWidth,6} " +
                    $"{"(did not complete: MaxNodeCount " + MaxNodeCount + " exceeded)",-52}");
            }
            catch (InvalidOperationException ex)
            {
                // A level's state table outgrew what .NET can allocate as a single array, before the
                // MaxNodeCount check ever got a chance to fire — the honest boundary this section
                // records is "did not complete", whichever of the two limits hit first.
                stopwatch.Stop();
                Console.WriteLine(
                    $"{name,-16} {graph.VertexCount,8} {graph.EdgeCount,7} {asGiven,8} {bfsWidth,6} " +
                    $"{"(did not complete: " + ex.Message + ")",-52}");
            }
        }

        /// <summary>
        /// Endpoints for the path count: the two farthest-apart (by hop count) vertices in the graph's
        /// largest connected component, found by BFS from every component representative. Nearest-neighbor
        /// construction at these vertex counts is connected in practice, but this does not assume it.
        /// </summary>
        private static (int S, int T) FarthestPairInLargestComponent(Graph graph)
        {
            var visited = new bool[graph.VertexCount];
            List<int>? bestComponent = null;

            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (visited[v])
                {
                    continue;
                }

                List<int> component = Bfs(graph, v, visited);
                if (bestComponent is null || component.Count > bestComponent.Count)
                {
                    bestComponent = component;
                }
            }

            int start = bestComponent![0];
            int[] distFromStart = Distances(graph, start);
            int a = bestComponent.OrderByDescending(v => distFromStart[v]).First();
            int[] distFromA = Distances(graph, a);
            int b = bestComponent.OrderByDescending(v => distFromA[v]).First();
            return (a, b);
        }

        private static List<int> Bfs(Graph graph, int start, bool[] visited)
        {
            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;

            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                component.Add(v);

                foreach (int edgeIndex in graph.IncidentEdges(v))
                {
                    Edge edge = graph.GetEdge(edgeIndex);
                    int other = edge.U == v ? edge.V : edge.U;
                    if (!visited[other])
                    {
                        visited[other] = true;
                        queue.Enqueue(other);
                    }
                }
            }

            return component;
        }

        private static int[] Distances(Graph graph, int start)
        {
            var dist = new int[graph.VertexCount];
            Array.Fill(dist, -1);
            dist[start] = 0;

            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (int edgeIndex in graph.IncidentEdges(v))
                {
                    Edge edge = graph.GetEdge(edgeIndex);
                    int other = edge.U == v ? edge.V : edge.U;
                    if (dist[other] < 0)
                    {
                        dist[other] = dist[v] + 1;
                        queue.Enqueue(other);
                    }
                }
            }

            return dist;
        }

        /// <summary>
        /// A nearest-neighbor construction similar in spirit to <see cref="EdgeOrderReport.GeometricGraph"/>
        /// (kept independent so this report's edge counts do not shift if that one changes), but bucketed
        /// into a uniform grid rather than sorting every other vertex by distance for each point: an
        /// O(n&#178; log n) full sort is impractical at the tens-of-thousands-of-vertices scale this report
        /// exercises. Each point instead only scans its own grid cell and an expanding ring of neighbors,
        /// which is an approximate (not exact) k-nearest-neighbor search &#8212; fine for a graph standing in
        /// for road-network structure, where the point is local connectivity, not an exact neighbor set.
        /// </summary>
        private static Graph RoadNetwork(int vertexCount, int k, int seed)
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

            // A uniform grid over the unit square, sized so a cell holds a handful of points on
            // average: each vertex's k nearest neighbors can then be found by scanning its own cell
            // plus an expanding ring of neighboring cells, instead of every other vertex.
            const int targetPointsPerCell = 8;
            int cellsPerSide = Math.Max(1, (int)Math.Sqrt(vertexCount / (double)targetPointsPerCell));
            double cellSize = 1.0 / cellsPerSide;
            int CellIndex(double v) => Math.Min(cellsPerSide - 1, (int)(v / cellSize));

            var cellOfVertex = new (int Cx, int Cy)[vertexCount];
            var buckets = new List<int>[cellsPerSide, cellsPerSide];
            for (int cx = 0; cx < cellsPerSide; cx++)
            {
                for (int cy = 0; cy < cellsPerSide; cy++)
                {
                    buckets[cx, cy] = new List<int>();
                }
            }

            for (int i = 0; i < vertexCount; i++)
            {
                int cx = CellIndex(x[i]);
                int cy = CellIndex(y[i]);
                cellOfVertex[i] = (cx, cy);
                buckets[cx, cy].Add(i);
            }

            var seen = new HashSet<Edge>();
            var edges = new List<Edge>();
            var candidates = new List<(double DistSq, int Index)>();

            for (int i = 0; i < vertexCount; i++)
            {
                (int cx, int cy) = cellOfVertex[i];

                // Expand the search radius (in cells) until at least k other points have been found.
                // Most points find enough neighbors at radius 1; only sparse regions near the grid's
                // edges need to widen further.
                int radius = 1;
                do
                {
                    candidates.Clear();
                    int minCx = Math.Max(0, cx - radius);
                    int maxCx = Math.Min(cellsPerSide - 1, cx + radius);
                    int minCy = Math.Max(0, cy - radius);
                    int maxCy = Math.Min(cellsPerSide - 1, cy + radius);

                    for (int ncx = minCx; ncx <= maxCx; ncx++)
                    {
                        for (int ncy = minCy; ncy <= maxCy; ncy++)
                        {
                            foreach (int j in buckets[ncx, ncy])
                            {
                                if (j == i)
                                {
                                    continue;
                                }

                                double dx = x[i] - x[j];
                                double dy = y[i] - y[j];
                                candidates.Add(((dx * dx) + (dy * dy), j));
                            }
                        }
                    }

                    radius++;
                }
                while (candidates.Count < k && radius <= cellsPerSide);

                candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
                for (int c = 0; c < candidates.Count && c < k; c++)
                {
                    int j = candidates[c].Index;
                    var edge = new Edge(Math.Min(i, j), Math.Max(i, j));
                    if (seen.Add(edge))
                    {
                        edges.Add(edge);
                    }
                }
            }

            return new Graph(vertexCount, edges);
        }

        private static long Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return GC.GetTotalMemory(forceFullCollection: true);
        }

        private static string Bytes(long bytes) => $"{bytes / (1024.0 * 1024.0):N1} MB";

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

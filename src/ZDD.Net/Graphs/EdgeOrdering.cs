using System;
using System.Collections.Generic;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// Computes the edge permutations behind <see cref="Graph.Optimize(EdgeOrderStrategy, EdgeOrderOptions)"/>
    /// (and <see cref="DirectedGraph.Optimize(EdgeOrderStrategy, EdgeOrderOptions)"/>) and evaluates how wide
    /// a frontier a candidate permutation would give.
    /// </summary>
    /// <remarks>
    /// Every strategy here is "visit the vertices in some order, and emit each edge as soon as both of its
    /// endpoints have been visited". A vertex leaves the frontier only after its last edge, so keeping the
    /// visit order local — one BFS layer, one DFS branch, one grid column — is what keeps the frontier
    /// narrow; the strategies differ only in what "local" means. Everything here works over
    /// <see cref="EdgeTopology"/> rather than <see cref="Graph"/> directly, since none of it needs to know
    /// whether — or which way — an edge is directed (docs/design/m7-directed-graphs.md §2.3).
    /// </remarks>
    internal static class EdgeOrdering
    {
        /// <summary>Returns the permutation <paramref name="strategy"/> produces: new edge index → <paramref name="topology"/>'s edge index.</summary>
        public static int[] Compute(EdgeTopology topology, EdgeOrderStrategy strategy, EdgeOrderOptions options)
        {
            switch (strategy)
            {
                case EdgeOrderStrategy.AsGiven:
                    return Identity(topology.EdgeCount);

                case EdgeOrderStrategy.Bfs:
                    return TraversalOrder(topology, depthFirst: false, options);

                case EdgeOrderStrategy.Dfs:
                    return TraversalOrder(topology, depthFirst: true, options);

                case EdgeOrderStrategy.Grid:
                    return TryGetGridShape(topology, out int rows, out int cols)
                        ? EdgeOrderFromVertexOrder(topology, SerpentineVertexOrder(rows, cols))
                        : TraversalOrder(topology, depthFirst: false, options);

                case EdgeOrderStrategy.BeamSearchPathWidth:
                    return BeamSearchPathWidth.Compute(topology, options);

                default:
                    throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Not a known edge-order strategy.");
            }
        }

        /// <summary>
        /// The peak frontier size <paramref name="topology"/> would have under <paramref name="order"/>
        /// (<see langword="null"/> for its own order), in <c>O(VertexCount + EdgeCount)</c>.
        /// </summary>
        /// <remarks>
        /// Counts the same thing <see cref="FrontierManager.MaxFrontierSize"/> does, without building the
        /// rest of its bookkeeping, so a candidate order can be scored without materializing a graph for it.
        /// </remarks>
        public static int MaxFrontierSize(EdgeTopology topology, int[]? order)
        {
            int edgeCount = topology.EdgeCount;
            if (edgeCount == 0)
            {
                return 0;
            }

            int vertexCount = topology.VertexCount;
            var firstPosition = new int[vertexCount];
            var lastPosition = new int[vertexCount];
            Array.Fill(firstPosition, -1);

            for (int i = 0; i < edgeCount; i++)
            {
                (int U, int V) edge = topology.Endpoints(order is null ? i : order[i]);

                if (firstPosition[edge.U] < 0)
                {
                    firstPosition[edge.U] = i;
                }

                if (firstPosition[edge.V] < 0)
                {
                    firstPosition[edge.V] = i;
                }

                lastPosition[edge.U] = i;
                lastPosition[edge.V] = i;
            }

            var introduced = new int[edgeCount];
            var forgotten = new int[edgeCount];
            for (int v = 0; v < vertexCount; v++)
            {
                if (firstPosition[v] >= 0)
                {
                    introduced[firstPosition[v]]++;
                    forgotten[lastPosition[v]]++;
                }
            }

            int frontier = 0;
            int max = 0;
            for (int i = 0; i < edgeCount; i++)
            {
                frontier += introduced[i];
                if (frontier > max)
                {
                    max = frontier;
                }

                frontier -= forgotten[i];
            }

            return max;
        }

        /// <summary>
        /// Recognizes a <c>rows</c> × <c>cols</c> grid numbered row-major, as <see cref="Graph.Grid"/>
        /// numbers one (vertex <c>(r, c)</c> is <c>r * cols + c</c>).
        /// </summary>
        /// <remarks>
        /// Only the row-major numbering is recognized — a grid whose vertices carry arbitrary labels is
        /// left to the BFS fallback. Column-major numbering is covered for free: it is the row-major
        /// numbering of the transposed grid, which is another factorization tried here. Matching is done
        /// against the set of <i>distinct</i> unordered endpoint pairs, not the raw edge count: for a
        /// <see cref="Graph"/> the two always agree (it rejects duplicate edges), but a
        /// <see cref="DirectedGraph"/> grid (<see cref="DirectedGraph.Grid"/>) carries each pair twice, as
        /// anti-parallel arcs.
        /// </remarks>
        public static bool TryGetGridShape(EdgeTopology topology, out int rows, out int cols)
        {
            int vertexCount = topology.VertexCount;

            var distinctPairs = new HashSet<(int, int)>();
            for (int i = 0; i < topology.EdgeCount; i++)
            {
                (int u, int v) = topology.Endpoints(i);
                distinctPairs.Add((Math.Min(u, v), Math.Max(u, v)));
            }

            for (int candidateRows = 1; candidateRows <= vertexCount; candidateRows++)
            {
                if (vertexCount % candidateRows != 0)
                {
                    continue;
                }

                int candidateCols = vertexCount / candidateRows;
                int expectedEdges = (candidateRows * (candidateCols - 1)) + ((candidateRows - 1) * candidateCols);
                if (distinctPairs.Count != expectedEdges)
                {
                    continue;
                }

                if (AllPairsAreGridEdges(distinctPairs, candidateCols))
                {
                    // The graph has as many distinct undirected pairs as the grid does and every one of
                    // them is a grid edge, so the two edge sets are equal.
                    rows = candidateRows;
                    cols = candidateCols;
                    return true;
                }
            }

            rows = 0;
            cols = 0;
            return false;
        }

        private static bool AllPairsAreGridEdges(HashSet<(int, int)> pairs, int cols)
        {
            foreach ((int low, int high) in pairs)
            {
                bool horizontal = high == low + 1 && low / cols == high / cols;
                bool vertical = high == low + cols;
                if (!horizontal && !vertical)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The serpentine visit order of a <paramref name="rows"/> × <paramref name="cols"/> grid: sweep
        /// along the longer side, reversing direction along the shorter one each step, so the frontier
        /// holds about one short side.
        /// </summary>
        private static int[] SerpentineVertexOrder(int rows, int cols)
        {
            var order = new int[rows * cols];
            int count = 0;

            if (rows <= cols)
            {
                for (int c = 0; c < cols; c++)
                {
                    for (int k = 0; k < rows; k++)
                    {
                        int r = c % 2 == 0 ? k : rows - 1 - k;
                        order[count++] = (r * cols) + c;
                    }
                }
            }
            else
            {
                for (int r = 0; r < rows; r++)
                {
                    for (int k = 0; k < cols; k++)
                    {
                        int c = r % 2 == 0 ? k : cols - 1 - k;
                        order[count++] = (r * cols) + c;
                    }
                }
            }

            return order;
        }

        private static int[] TraversalOrder(EdgeTopology topology, bool depthFirst, EdgeOrderOptions options)
        {
            switch (options.Selection)
            {
                case StartVertexSelection.Specified:
                    if ((uint)options.StartVertex >= (uint)topology.VertexCount)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(options),
                            options.StartVertex,
                            $"The start vertex must be in 0 .. {topology.VertexCount - 1}.");
                    }

                    return EdgeOrderFromVertexOrder(topology, VertexOrder(topology, depthFirst, options.StartVertex));

                case StartVertexSelection.BestOfCandidates:
                    return BestTraversalOrder(topology, depthFirst, options.MaxCandidates);

                default:
                    return EdgeOrderFromVertexOrder(topology, VertexOrder(topology, depthFirst, MinimumDegreeVertex(topology)));
            }
        }

        /// <summary>
        /// Every vertex touching at least one edge, lowest degree first (ties broken by index): the order
        /// candidate start vertices are tried in, since starting at a low-degree vertex tends to keep the
        /// first frontier small.
        /// </summary>
        internal static List<int> DegreeSortedVertices(EdgeTopology topology)
        {
            var vertices = new List<int>();
            for (int v = 0; v < topology.VertexCount; v++)
            {
                if (topology.Degree(v) > 0)
                {
                    vertices.Add(v);
                }
            }

            vertices.Sort((left, right) =>
            {
                int byDegree = topology.Degree(left).CompareTo(topology.Degree(right));
                return byDegree != 0 ? byDegree : left.CompareTo(right);
            });

            return vertices;
        }

        /// <summary>Tries the lowest-degree start vertices and keeps the order with the smallest peak frontier.</summary>
        private static int[] BestTraversalOrder(EdgeTopology topology, bool depthFirst, int maxCandidates)
        {
            List<int> candidates = DegreeSortedVertices(topology);

            if (candidates.Count == 0)
            {
                // No edges at all: every order is the empty one.
                return Identity(topology.EdgeCount);
            }

            int tried = maxCandidates > 0 ? Math.Min(maxCandidates, candidates.Count) : candidates.Count;
            int[]? best = null;
            int bestWidth = int.MaxValue;

            for (int i = 0; i < tried; i++)
            {
                int[] order = EdgeOrderFromVertexOrder(topology, VertexOrder(topology, depthFirst, candidates[i]));
                int width = MaxFrontierSize(topology, order);
                if (width < bestWidth)
                {
                    bestWidth = width;
                    best = order;
                }
            }

            return best!;
        }

        private static int MinimumDegreeVertex(EdgeTopology topology)
        {
            int best = 0;
            int bestDegree = int.MaxValue;

            for (int v = 0; v < topology.VertexCount; v++)
            {
                int degree = topology.Degree(v);
                if (degree > 0 && degree < bestDegree)
                {
                    bestDegree = degree;
                    best = v;
                }
            }

            return best;
        }

        /// <summary>
        /// Visits every vertex, starting at <paramref name="start"/> and continuing from the lowest
        /// unvisited vertex once a component is exhausted, so disconnected graphs and isolated vertices
        /// are ordinary cases rather than special ones.
        /// </summary>
        private static int[] VertexOrder(EdgeTopology topology, bool depthFirst, int start)
        {
            int vertexCount = topology.VertexCount;
            var order = new int[vertexCount];
            var visited = new bool[vertexCount];
            int count = 0;

            var queue = new Queue<int>();
            var stack = new Stack<int>();

            for (int seedIndex = -1; seedIndex < vertexCount; seedIndex++)
            {
                int seed = seedIndex < 0 ? start : seedIndex;
                if (visited[seed])
                {
                    continue;
                }

                if (depthFirst)
                {
                    stack.Push(seed);
                    while (stack.Count > 0)
                    {
                        int v = stack.Pop();
                        if (visited[v])
                        {
                            continue;
                        }

                        visited[v] = true;
                        order[count++] = v;

                        // Pushed in reverse so the first incident edge is the one explored first.
                        IReadOnlyList<int> incident = topology.IncidentEdges(v);
                        for (int k = incident.Count - 1; k >= 0; k--)
                        {
                            int w = topology.Other(incident[k], v);
                            if (!visited[w])
                            {
                                stack.Push(w);
                            }
                        }
                    }
                }
                else
                {
                    visited[seed] = true;
                    order[count++] = seed;
                    queue.Enqueue(seed);

                    while (queue.Count > 0)
                    {
                        int v = queue.Dequeue();
                        foreach (int edgeIndex in topology.IncidentEdges(v))
                        {
                            int w = topology.Other(edgeIndex, v);
                            if (!visited[w])
                            {
                                visited[w] = true;
                                order[count++] = w;
                                queue.Enqueue(w);
                            }
                        }
                    }
                }
            }

            return order;
        }

        /// <summary>
        /// Emits each edge at the point its second endpoint is visited, which is the earliest position at
        /// which the edge can be decided — and therefore the earliest its endpoints can leave the frontier.
        /// </summary>
        internal static int[] EdgeOrderFromVertexOrder(EdgeTopology topology, int[] vertexOrder)
        {
            var order = new int[topology.EdgeCount];
            var visited = new bool[topology.VertexCount];
            int count = 0;

            foreach (int v in vertexOrder)
            {
                visited[v] = true;
                foreach (int edgeIndex in topology.IncidentEdges(v))
                {
                    if (visited[topology.Other(edgeIndex, v)])
                    {
                        order[count++] = edgeIndex;
                    }
                }
            }

            return order;
        }

        internal static int[] Identity(int count)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++)
            {
                order[i] = i;
            }

            return order;
        }
    }
}

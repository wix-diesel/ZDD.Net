using System;
using System.Collections.Generic;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// Computes the edge permutations behind <see cref="Graph.Optimize(EdgeOrderStrategy, EdgeOrderOptions)"/>
    /// and evaluates how wide a frontier a candidate permutation would give.
    /// </summary>
    /// <remarks>
    /// Every strategy here is "visit the vertices in some order, and emit each edge as soon as both of its
    /// endpoints have been visited". A vertex leaves the frontier only after its last edge, so keeping the
    /// visit order local — one BFS layer, one DFS branch, one grid column — is what keeps the frontier
    /// narrow; the strategies differ only in what "local" means.
    /// </remarks>
    internal static class EdgeOrdering
    {
        /// <summary>Returns the permutation <paramref name="strategy"/> produces: new edge index → <paramref name="graph"/>'s edge index.</summary>
        public static int[] Compute(Graph graph, EdgeOrderStrategy strategy, EdgeOrderOptions options)
        {
            switch (strategy)
            {
                case EdgeOrderStrategy.AsGiven:
                    return Identity(graph.EdgeCount);

                case EdgeOrderStrategy.Bfs:
                    return TraversalOrder(graph, depthFirst: false, options);

                case EdgeOrderStrategy.Dfs:
                    return TraversalOrder(graph, depthFirst: true, options);

                case EdgeOrderStrategy.Grid:
                    return TryGetGridShape(graph, out int rows, out int cols)
                        ? EdgeOrderFromVertexOrder(graph, SerpentineVertexOrder(rows, cols))
                        : TraversalOrder(graph, depthFirst: false, options);

                case EdgeOrderStrategy.BeamSearchPathWidth:
                    return BeamSearchPathWidth.Compute(graph, options);

                default:
                    throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Not a known edge-order strategy.");
            }
        }

        /// <summary>
        /// The peak frontier size <paramref name="graph"/> would have under <paramref name="order"/>
        /// (<see langword="null"/> for the graph's own order), in <c>O(VertexCount + EdgeCount)</c>.
        /// </summary>
        /// <remarks>
        /// Counts the same thing <see cref="FrontierManager.MaxFrontierSize"/> does, without building the
        /// rest of its bookkeeping, so a candidate order can be scored without materializing a graph for it.
        /// </remarks>
        public static int MaxFrontierSize(Graph graph, int[]? order)
        {
            int edgeCount = graph.EdgeCount;
            if (edgeCount == 0)
            {
                return 0;
            }

            int vertexCount = graph.VertexCount;
            var firstPosition = new int[vertexCount];
            var lastPosition = new int[vertexCount];
            Array.Fill(firstPosition, -1);

            for (int i = 0; i < edgeCount; i++)
            {
                Edge edge = graph.GetEdge(order is null ? i : order[i]);

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
        /// numbering of the transposed grid, which is another factorization tried here.
        /// </remarks>
        public static bool TryGetGridShape(Graph graph, out int rows, out int cols)
        {
            int vertexCount = graph.VertexCount;

            for (int candidateRows = 1; candidateRows <= vertexCount; candidateRows++)
            {
                if (vertexCount % candidateRows != 0)
                {
                    continue;
                }

                int candidateCols = vertexCount / candidateRows;
                int expectedEdges = (candidateRows * (candidateCols - 1)) + ((candidateRows - 1) * candidateCols);
                if (graph.EdgeCount != expectedEdges)
                {
                    continue;
                }

                if (AllEdgesAreGridEdges(graph, candidateCols))
                {
                    // The graph has as many distinct edges as the grid does and every one of them is a grid
                    // edge, so the two edge sets are equal (Graph rejects duplicate edges).
                    rows = candidateRows;
                    cols = candidateCols;
                    return true;
                }
            }

            rows = 0;
            cols = 0;
            return false;
        }

        private static bool AllEdgesAreGridEdges(Graph graph, int cols)
        {
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);
                int low = Math.Min(edge.U, edge.V);
                int high = Math.Max(edge.U, edge.V);

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

        private static int[] TraversalOrder(Graph graph, bool depthFirst, EdgeOrderOptions options)
        {
            switch (options.Selection)
            {
                case StartVertexSelection.Specified:
                    if ((uint)options.StartVertex >= (uint)graph.VertexCount)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(options),
                            options.StartVertex,
                            $"The start vertex must be in 0 .. {graph.VertexCount - 1}.");
                    }

                    return EdgeOrderFromVertexOrder(graph, VertexOrder(graph, depthFirst, options.StartVertex));

                case StartVertexSelection.BestOfCandidates:
                    return BestTraversalOrder(graph, depthFirst, options.MaxCandidates);

                default:
                    return EdgeOrderFromVertexOrder(graph, VertexOrder(graph, depthFirst, MinimumDegreeVertex(graph)));
            }
        }

        /// <summary>
        /// Every vertex touching at least one edge, lowest degree first (ties broken by index): the order
        /// candidate start vertices are tried in, since starting at a low-degree vertex tends to keep the
        /// first frontier small.
        /// </summary>
        internal static List<int> DegreeSortedVertices(Graph graph)
        {
            var vertices = new List<int>();
            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (graph.Degree(v) > 0)
                {
                    vertices.Add(v);
                }
            }

            vertices.Sort((left, right) =>
            {
                int byDegree = graph.Degree(left).CompareTo(graph.Degree(right));
                return byDegree != 0 ? byDegree : left.CompareTo(right);
            });

            return vertices;
        }

        /// <summary>Tries the lowest-degree start vertices and keeps the order with the smallest peak frontier.</summary>
        private static int[] BestTraversalOrder(Graph graph, bool depthFirst, int maxCandidates)
        {
            List<int> candidates = DegreeSortedVertices(graph);

            if (candidates.Count == 0)
            {
                // No edges at all: every order is the empty one.
                return Identity(graph.EdgeCount);
            }

            int tried = maxCandidates > 0 ? Math.Min(maxCandidates, candidates.Count) : candidates.Count;
            int[]? best = null;
            int bestWidth = int.MaxValue;

            for (int i = 0; i < tried; i++)
            {
                int[] order = EdgeOrderFromVertexOrder(graph, VertexOrder(graph, depthFirst, candidates[i]));
                int width = MaxFrontierSize(graph, order);
                if (width < bestWidth)
                {
                    bestWidth = width;
                    best = order;
                }
            }

            return best!;
        }

        private static int MinimumDegreeVertex(Graph graph)
        {
            int best = 0;
            int bestDegree = int.MaxValue;

            for (int v = 0; v < graph.VertexCount; v++)
            {
                int degree = graph.Degree(v);
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
        private static int[] VertexOrder(Graph graph, bool depthFirst, int start)
        {
            int vertexCount = graph.VertexCount;
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
                        IReadOnlyList<int> incident = graph.IncidentEdges(v);
                        for (int k = incident.Count - 1; k >= 0; k--)
                        {
                            int w = graph.GetEdge(incident[k]).Other(v);
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
                        foreach (int edgeIndex in graph.IncidentEdges(v))
                        {
                            int w = graph.GetEdge(edgeIndex).Other(v);
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
        internal static int[] EdgeOrderFromVertexOrder(Graph graph, int[] vertexOrder)
        {
            var order = new int[graph.EdgeCount];
            var visited = new bool[graph.VertexCount];
            int count = 0;

            foreach (int v in vertexOrder)
            {
                visited[v] = true;
                foreach (int edgeIndex in graph.IncidentEdges(v))
                {
                    if (visited[graph.GetEdge(edgeIndex).Other(v)])
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

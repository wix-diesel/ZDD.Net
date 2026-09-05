using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// A directed, simple graph, stored as an arc list. Anti-parallel arcs (<c>u -&gt; v</c> and
    /// <c>v -&gt; u</c> both present) are allowed; self-loops and multi-arcs (the same <c>u -&gt; v</c>
    /// twice) are rejected, matching <see cref="Graph"/>'s undirected rules (see
    /// <c>docs/design/m7-directed-graphs.md</c> §1).
    /// </summary>
    /// <remarks>
    /// Besides the data structure (arc list, adjacency, undirected/directed conversions), this type shares
    /// <see cref="Graph"/>'s edge-order API (<see cref="WithEdgeOrder"/> / <see cref="Optimize"/> /
    /// <see cref="EstimateMaxFrontierSize()"/> / <see cref="SourceOrder"/>) and frontier-method support
    /// (<see cref="FrontierManager"/>), both built on the <see cref="Topology"/> this graph and
    /// <see cref="Graph"/> both expose internally (docs/design/m7-directed-graphs.md §2.3).
    /// </remarks>
    public sealed class DirectedGraph
    {
        private readonly DirectedEdge[] _edges;
        private readonly int[][] _outgoingByVertex;
        private readonly int[][] _incomingByVertex;
        private readonly int[][] _incidentByVertex;
        private readonly ReadOnlyCollection<DirectedEdge> _edgesView;
        private readonly ReadOnlyCollection<int>[] _outgoingByVertexView;
        private readonly ReadOnlyCollection<int>[] _incomingByVertexView;
        private readonly ReadOnlyCollection<int>[] _incidentByVertexView;

        /// <summary>Creates a directed graph from an explicit vertex count and arc list.</summary>
        /// <param name="vertexCount">The number of vertices; must be positive. Vertices are indexed <c>0 .. vertexCount - 1</c>.</param>
        /// <param name="edges">
        /// The arcs, in the order that becomes the frontier method's variable order — see
        /// <see cref="WithEdgeOrder"/> / <see cref="Optimize"/>, which reorder it. Copied, so later
        /// mutating a collection passed in has no effect on the graph.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertexCount"/> is not positive.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="edges"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// An arc has an endpoint outside <c>0 .. vertexCount - 1</c>, is a self-loop, or duplicates
        /// (same <see cref="DirectedEdge.From"/> and <see cref="DirectedEdge.To"/>) another arc in
        /// <paramref name="edges"/>. The anti-parallel arc <c>v -&gt; u</c> of an arc <c>u -&gt; v</c> is
        /// not a duplicate and is allowed.
        /// </exception>
        public DirectedGraph(int vertexCount, IEnumerable<DirectedEdge> edges)
        {
            if (vertexCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount), vertexCount, "The vertex count must be positive.");
            }

            ArgumentNullException.ThrowIfNull(edges);

            VertexCount = vertexCount;
            _edges = System.Linq.Enumerable.ToArray(edges);

            var seen = new HashSet<DirectedEdge>();
            var outCounts = new int[vertexCount];
            var inCounts = new int[vertexCount];

            for (int i = 0; i < _edges.Length; i++)
            {
                DirectedEdge edge = _edges[i];

                if ((uint)edge.From >= (uint)vertexCount)
                {
                    throw new ArgumentException($"Arc {i} has endpoint {edge.From}, outside 0 .. {vertexCount - 1}.", nameof(edges));
                }

                if ((uint)edge.To >= (uint)vertexCount)
                {
                    throw new ArgumentException($"Arc {i} has endpoint {edge.To}, outside 0 .. {vertexCount - 1}.", nameof(edges));
                }

                if (edge.From == edge.To)
                {
                    throw new ArgumentException($"Arc {i} is a self-loop at vertex {edge.From}; self-loops are not supported.", nameof(edges));
                }

                if (!seen.Add(edge))
                {
                    throw new ArgumentException($"Arc {i} ({edge}) duplicates an earlier arc; multi-arcs are not supported.", nameof(edges));
                }

                outCounts[edge.From]++;
                inCounts[edge.To]++;
            }

            _outgoingByVertex = new int[vertexCount][];
            _incomingByVertex = new int[vertexCount][];
            _incidentByVertex = new int[vertexCount][];
            for (int v = 0; v < vertexCount; v++)
            {
                _outgoingByVertex[v] = new int[outCounts[v]];
                _incomingByVertex[v] = new int[inCounts[v]];
                _incidentByVertex[v] = new int[outCounts[v] + inCounts[v]];
            }

            var outFill = new int[vertexCount];
            var inFill = new int[vertexCount];
            var incidentFill = new int[vertexCount];
            for (int i = 0; i < _edges.Length; i++)
            {
                DirectedEdge edge = _edges[i];
                _outgoingByVertex[edge.From][outFill[edge.From]++] = i;
                _incomingByVertex[edge.To][inFill[edge.To]++] = i;
                _incidentByVertex[edge.From][incidentFill[edge.From]++] = i;
                _incidentByVertex[edge.To][incidentFill[edge.To]++] = i;
            }

            _edgesView = new ReadOnlyCollection<DirectedEdge>(_edges);
            _outgoingByVertexView = new ReadOnlyCollection<int>[vertexCount];
            _incomingByVertexView = new ReadOnlyCollection<int>[vertexCount];
            _incidentByVertexView = new ReadOnlyCollection<int>[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                _outgoingByVertexView[v] = new ReadOnlyCollection<int>(_outgoingByVertex[v]);
                _incomingByVertexView[v] = new ReadOnlyCollection<int>(_incomingByVertex[v]);
                _incidentByVertexView[v] = new ReadOnlyCollection<int>(_incidentByVertex[v]);
            }

            var endpoints = new (int U, int V)[_edges.Length];
            for (int i = 0; i < _edges.Length; i++)
            {
                endpoints[i] = (_edges[i].From, _edges[i].To);
            }

            Topology = new EdgeTopology(vertexCount, endpoints, _incidentByVertexView);
        }

        /// <summary>Creates the graph <see cref="WithEdgeOrder"/> returns: the same graph reordered, remembering where its arcs came from.</summary>
        private DirectedGraph(int vertexCount, DirectedEdge[] edges, DirectedEdgeOrderMapping sourceOrder)
            : this(vertexCount, edges)
        {
            SourceOrder = sourceOrder;
        }

        /// <summary>The number of vertices, indexed <c>0 .. VertexCount - 1</c>.</summary>
        public int VertexCount { get; }

        /// <summary>The number of arcs.</summary>
        public int EdgeCount => _edges.Length;

        /// <summary>The arcs, in variable order (arc index <c>i</c> is variable index <c>i</c>).</summary>
        /// <remarks>A read-only view over the backing storage: it cannot be downcast to mutate the graph.</remarks>
        public IReadOnlyList<DirectedEdge> Edges => _edgesView;

        /// <summary>
        /// The direction-agnostic view of this graph's arcs that <see cref="FrontierManager"/> and
        /// <see cref="EdgeOrdering"/> build on, shared with <see cref="Graph.Topology"/>.
        /// </summary>
        internal EdgeTopology Topology { get; }

        /// <summary>
        /// How this graph's arc indices map back to the graph it was reordered from, or
        /// <see langword="null"/> if it was constructed directly rather than by
        /// <see cref="Optimize"/> / <see cref="WithEdgeOrder"/>.
        /// </summary>
        public DirectedEdgeOrderMapping? SourceOrder { get; }

        /// <summary>Returns the arc at <paramref name="edgeIndex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        public DirectedEdge GetEdge(int edgeIndex)
        {
            if ((uint)edgeIndex >= (uint)_edges.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeIndex), edgeIndex, $"Must be in 0 .. {_edges.Length - 1}.");
            }

            return _edges[edgeIndex];
        }

        /// <summary>The indices (into <see cref="Edges"/>) of the arcs leaving <paramref name="vertex"/>.</summary>
        /// <remarks>A read-only view over the backing storage: it cannot be downcast to mutate the graph.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public IReadOnlyList<int> OutgoingEdges(int vertex)
        {
            CheckVertex(vertex);
            return _outgoingByVertexView[vertex];
        }

        /// <summary>The indices (into <see cref="Edges"/>) of the arcs entering <paramref name="vertex"/>.</summary>
        /// <remarks>A read-only view over the backing storage: it cannot be downcast to mutate the graph.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public IReadOnlyList<int> IncomingEdges(int vertex)
        {
            CheckVertex(vertex);
            return _incomingByVertexView[vertex];
        }

        /// <summary>
        /// The indices (into <see cref="Edges"/>) of the arcs incident to <paramref name="vertex"/>,
        /// regardless of direction, in arc order (the order the arcs were constructed with).
        /// </summary>
        /// <remarks>A read-only view over the backing storage: it cannot be downcast to mutate the graph.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public IReadOnlyList<int> IncidentEdges(int vertex)
        {
            CheckVertex(vertex);
            return _incidentByVertexView[vertex];
        }

        /// <summary>The number of arcs leaving <paramref name="vertex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public int OutDegree(int vertex) => OutgoingEdges(vertex).Count;

        /// <summary>The number of arcs entering <paramref name="vertex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public int InDegree(int vertex) => IncomingEdges(vertex).Count;

        private void CheckVertex(int vertex)
        {
            if ((uint)vertex >= (uint)VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(vertex), vertex, $"Must be in 0 .. {VertexCount - 1}.");
            }
        }

        /// <summary>
        /// Returns a graph with the same vertices and arcs, reordered by <paramref name="edgeOrder"/>,
        /// carrying a <see cref="SourceOrder"/> back to this graph. This graph is left untouched.
        /// </summary>
        /// <param name="edgeOrder">
        /// A permutation of <c>0 .. EdgeCount - 1</c>: the new graph's arc <c>i</c> is this graph's arc
        /// <c>edgeOrder[i]</c>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="edgeOrder"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="edgeOrder"/> is not a permutation of <c>0 .. EdgeCount - 1</c>.</exception>
        public DirectedGraph WithEdgeOrder(IReadOnlyList<int> edgeOrder)
        {
            ArgumentNullException.ThrowIfNull(edgeOrder);

            if (edgeOrder.Count != EdgeCount)
            {
                throw new ArgumentException($"Expected {EdgeCount} indices, got {edgeOrder.Count}.", nameof(edgeOrder));
            }

            var seen = new bool[EdgeCount];
            var toSource = new int[EdgeCount];
            var reordered = new DirectedEdge[EdgeCount];
            for (int i = 0; i < EdgeCount; i++)
            {
                int source = edgeOrder[i];
                if ((uint)source >= (uint)EdgeCount || seen[source])
                {
                    throw new ArgumentException("Not a permutation of 0 .. EdgeCount - 1.", nameof(edgeOrder));
                }

                seen[source] = true;
                toSource[i] = source;
                reordered[i] = _edges[source];
            }

            return new DirectedGraph(VertexCount, reordered, new DirectedEdgeOrderMapping(this, toSource));
        }

        /// <summary>
        /// Returns a copy of this graph whose arcs are reordered by <paramref name="strategy"/> to keep the
        /// frontier narrow. This graph is left untouched.
        /// </summary>
        /// <remarks>
        /// <b>The returned graph renumbers the arcs</b> exactly as <see cref="Graph.Optimize"/> renumbers
        /// edges — read a result built over it back through <see cref="SourceOrder"/>
        /// (<see cref="DirectedEdgeOrderMapping.ToSourceEdgeIndex"/>) before interpreting it against this
        /// graph. <see cref="EdgeOrderStrategy.Grid"/> falls back to <see cref="EdgeOrderStrategy.Bfs"/>
        /// unless the graph is <see cref="Bidirected"/> from a grid numbered row-major (as <see cref="Grid"/>
        /// numbers one) — a one-way-street grid is not recognized and falls back to BFS too.
        /// </remarks>
        /// <param name="strategy">The ordering heuristic; <see cref="EdgeOrderStrategy.Bfs"/> by default.</param>
        /// <param name="options">Which vertex the traversal starts from; minimum degree by default.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="strategy"/> is not a known strategy, or a specified start vertex is outside <c>0 .. VertexCount - 1</c>.</exception>
        public DirectedGraph Optimize(EdgeOrderStrategy strategy = EdgeOrderStrategy.Bfs, EdgeOrderOptions options = default) =>
            WithEdgeOrder(EdgeOrdering.Compute(Topology, strategy, options));

        /// <summary>
        /// The peak frontier size this graph's arc order implies — the same quantity as
        /// <see cref="Graph.EstimateMaxFrontierSize()"/>, computed over arcs instead of edges. Runs in
        /// <c>O(VertexCount + EdgeCount)</c>.
        /// </summary>
        public int EstimateMaxFrontierSize() => EdgeOrdering.MaxFrontierSize(Topology, null);

        /// <summary>
        /// The peak frontier size <paramref name="strategy"/> would achieve, without building the reordered
        /// graph — for comparing strategies before picking one.
        /// </summary>
        /// <param name="strategy">The ordering heuristic to evaluate.</param>
        /// <param name="options">Which vertex the traversal starts from; minimum degree by default.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="strategy"/> is not a known strategy, or a specified start vertex is outside <c>0 .. VertexCount - 1</c>.</exception>
        public int EstimateMaxFrontierSize(EdgeOrderStrategy strategy, EdgeOrderOptions options = default) =>
            EdgeOrdering.MaxFrontierSize(Topology, EdgeOrdering.Compute(Topology, strategy, options));

        /// <summary>
        /// Collapses this graph to an undirected <see cref="Graph"/> over the same vertices: an
        /// anti-parallel pair <c>u -&gt; v</c> / <c>v -&gt; u</c> becomes the single undirected edge
        /// <c>(u, v)</c>, so <b>the edge count can drop</b> relative to <see cref="EdgeCount"/>.
        /// </summary>
        /// <remarks>
        /// Because arcs can merge, there is no well-defined mapping from the result's edge indices back to
        /// this graph's arc indices, so the result's <see cref="Graph.SourceOrder"/> is always
        /// <see langword="null"/> — this deliberately keeps it out of <c>GraphSet.ToEdgeOrder</c> (M6-6),
        /// which assumes a source order it does not have. Use this only to inspect rough undirected
        /// structure (e.g. for debugging) or as a building block for edge-order computation; it is not a
        /// faithful inverse of an arbitrary directed graph — only of <see cref="Bidirected"/>.
        /// </remarks>
        public Graph ToUndirected()
        {
            var undirectedEdges = new List<Edge>(_edges.Length);
            var seen = new HashSet<Edge>(_edges.Length);
            foreach (DirectedEdge edge in _edges)
            {
                Edge undirected = edge.AsUndirected();
                if (seen.Add(undirected))
                {
                    undirectedEdges.Add(undirected);
                }
            }

            return new Graph(VertexCount, undirectedEdges);
        }

        /// <summary>
        /// Creates a directed graph by replacing every undirected edge <c>(u, v)</c> of
        /// <paramref name="graph"/> with the two anti-parallel arcs <c>u -&gt; v</c> and <c>v -&gt; u</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public static DirectedGraph Bidirected(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            var edges = new List<DirectedEdge>(graph.EdgeCount * 2);
            foreach (Edge edge in graph.Edges)
            {
                edges.Add(new DirectedEdge(edge.U, edge.V));
                edges.Add(new DirectedEdge(edge.V, edge.U));
            }

            return new DirectedGraph(graph.VertexCount, edges);
        }

        /// <summary>Creates an <c>rows</c> × <c>cols</c> grid graph with every edge open to both directions.</summary>
        /// <remarks>Equivalent to <c>Bidirected(Graph.Grid(rows, cols))</c>; see <see cref="Graph.Grid"/> for vertex/edge layout.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> or <paramref name="cols"/> is not positive.</exception>
        public static DirectedGraph Grid(int rows, int cols) => Bidirected(Graph.Grid(rows, cols));

        /// <summary>Creates the directed graph on <paramref name="n"/> vertices with an arc for every ordered pair of distinct vertices.</summary>
        /// <remarks>Arcs are ordered lexicographically by <c>(from, to)</c>.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is not positive.</exception>
        public static DirectedGraph Complete(int n)
        {
            if (n <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(n), n, "Must be positive.");
            }

            var edges = new List<DirectedEdge>(n * (n - 1));
            for (int u = 0; u < n; u++)
            {
                for (int v = 0; v < n; v++)
                {
                    if (u != v)
                    {
                        edges.Add(new DirectedEdge(u, v));
                    }
                }
            }

            return new DirectedGraph(n, edges);
        }

        /// <summary>Creates a one-directional cycle on <paramref name="n"/> vertices: <c>0 -&gt; 1 -&gt; 2 -&gt; ... -&gt; (n-1) -&gt; 0</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="n"/> is less than 2 (below that, the arc back to <c>0</c> would be a self-loop).
        /// Unlike <see cref="Graph.Cycle"/>, <c>n = 2</c> is valid here: the two arcs <c>0 -&gt; 1</c> and
        /// <c>1 -&gt; 0</c> are anti-parallel, not duplicates.
        /// </exception>
        public static DirectedGraph Cycle(int n)
        {
            if (n < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(n), n, "Must be at least 2.");
            }

            var edges = new List<DirectedEdge>(n);
            for (int i = 0; i < n; i++)
            {
                edges.Add(new DirectedEdge(i, (i + 1) % n));
            }

            return new DirectedGraph(n, edges);
        }

        /// <summary>Creates a one-directional path on <paramref name="n"/> vertices: <c>0 -&gt; 1 -&gt; 2 -&gt; ... -&gt; (n-1)</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is not positive.</exception>
        public static DirectedGraph Path(int n)
        {
            if (n <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(n), n, "Must be positive.");
            }

            var edges = new List<DirectedEdge>(Math.Max(0, n - 1));
            for (int i = 0; i < n - 1; i++)
            {
                edges.Add(new DirectedEdge(i, i + 1));
            }

            return new DirectedGraph(n, edges);
        }
    }
}

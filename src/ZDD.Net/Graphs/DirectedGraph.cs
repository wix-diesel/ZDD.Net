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
    /// This type carries only the data structure: arc list, adjacency, and the undirected/directed
    /// conversions. The edge-order API that <see cref="Graph"/> exposes (<c>WithEdgeOrder</c> /
    /// <c>Optimize</c> / <c>EstimateMaxFrontierSize</c> / <c>SourceOrder</c>) and frontier-method support
    /// are deferred to a later milestone that generalizes <c>EdgeOrdering</c> and
    /// <c>FrontierManager</c> to work over either graph type.
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
        /// The arcs. Copied, so later mutating a collection passed in has no effect on the graph.
        /// Order carries no significance yet (there is no edge-order API on this type); it is kept
        /// only so <see cref="Edges"/> and <see cref="GetEdge"/> are stable and predictable.
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
        }

        /// <summary>The number of vertices, indexed <c>0 .. VertexCount - 1</c>.</summary>
        public int VertexCount { get; }

        /// <summary>The number of arcs.</summary>
        public int EdgeCount => _edges.Length;

        /// <summary>The arcs, in construction order.</summary>
        /// <remarks>A read-only view over the backing storage: it cannot be downcast to mutate the graph.</remarks>
        public IReadOnlyList<DirectedEdge> Edges => _edgesView;

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
        /// regardless of direction (outgoing arcs first, then incoming, each in arc order).
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
            var undirectedEdges = new List<Edge>();
            var seen = new HashSet<Edge>();
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

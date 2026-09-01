using System;
using System.Collections.Generic;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// An undirected, simple graph, stored as an edge list. Edge order is preserved exactly as given and
    /// is significant: it is the frontier method's variable order, not an incidental detail.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Edge index ↔ variable index</b>: edge index <c>i</c> (the position of an edge in <see cref="Edges"/>)
    /// <i>is</i> the ZDD variable index <c>i</c> that <see cref="Frontier.IDdSpec{TState}"/> works with — see
    /// <see cref="EdgeIndexToVariableIndex"/>. It is an identity, kept as a named conversion so graph specs
    /// never have to reason about which integer means what.
    /// </para>
    /// <para>
    /// <b>Edge index ↔ level</b>: internally, ZDD levels run <c>1</c> (leaf side) .. <c>EdgeCount</c> (root
    /// side, PLAN.md §4.1), while the frontier method decides edges in order starting from edge <c>0</c> at
    /// the root. So edge index and level run in <i>opposite</i> directions: edge <c>0</c> is level
    /// <c>EdgeCount</c>, and edge <c>EdgeCount - 1</c> is level <c>1</c>. Use
    /// <see cref="EdgeIndexToLevel"/> / <see cref="LevelToEdgeIndex"/> rather than re-deriving this — it is
    /// the single place this reversal is allowed to live.
    /// </para>
    /// </remarks>
    public sealed class Graph
    {
        private readonly Edge[] _edges;
        private readonly int[][] _incidentEdgesByVertex;

        /// <summary>Creates a graph from an explicit vertex count and edge list.</summary>
        /// <param name="vertexCount">The number of vertices; must be positive. Vertices are indexed <c>0 .. vertexCount - 1</c>.</param>
        /// <param name="edges">
        /// The edges, in the order that becomes the frontier method's variable order. Copied, so later
        /// mutating a collection passed in has no effect on the graph.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertexCount"/> is not positive.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="edges"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// An edge has an endpoint outside <c>0 .. vertexCount - 1</c>, is a self-loop, or duplicates
        /// (as an unordered pair) another edge in <paramref name="edges"/>.
        /// </exception>
        public Graph(int vertexCount, IEnumerable<Edge> edges)
        {
            if (vertexCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexCount), vertexCount, "The vertex count must be positive.");
            }

            ArgumentNullException.ThrowIfNull(edges);

            VertexCount = vertexCount;
            _edges = System.Linq.Enumerable.ToArray(edges);

            var seen = new HashSet<Edge>();
            var incidentCounts = new int[vertexCount];

            for (int i = 0; i < _edges.Length; i++)
            {
                Edge edge = _edges[i];

                if ((uint)edge.U >= (uint)vertexCount)
                {
                    throw new ArgumentException($"Edge {i} has endpoint {edge.U}, outside 0 .. {vertexCount - 1}.", nameof(edges));
                }

                if ((uint)edge.V >= (uint)vertexCount)
                {
                    throw new ArgumentException($"Edge {i} has endpoint {edge.V}, outside 0 .. {vertexCount - 1}.", nameof(edges));
                }

                if (edge.U == edge.V)
                {
                    throw new ArgumentException($"Edge {i} is a self-loop at vertex {edge.U}; self-loops are not supported.", nameof(edges));
                }

                if (!seen.Add(edge))
                {
                    throw new ArgumentException($"Edge {i} ({edge}) duplicates an earlier edge; multi-edges are not supported.", nameof(edges));
                }

                incidentCounts[edge.U]++;
                incidentCounts[edge.V]++;
            }

            _incidentEdgesByVertex = new int[vertexCount][];
            for (int v = 0; v < vertexCount; v++)
            {
                _incidentEdgesByVertex[v] = new int[incidentCounts[v]];
            }

            var fillIndex = new int[vertexCount];
            for (int i = 0; i < _edges.Length; i++)
            {
                Edge edge = _edges[i];
                _incidentEdgesByVertex[edge.U][fillIndex[edge.U]++] = i;
                _incidentEdgesByVertex[edge.V][fillIndex[edge.V]++] = i;
            }
        }

        /// <summary>The number of vertices, indexed <c>0 .. VertexCount - 1</c>.</summary>
        public int VertexCount { get; }

        /// <summary>The number of edges.</summary>
        public int EdgeCount => _edges.Length;

        /// <summary>The edges, in variable order (edge index <c>i</c> is variable index <c>i</c>).</summary>
        public IReadOnlyList<Edge> Edges => _edges;

        /// <summary>Returns the edge at <paramref name="edgeIndex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        public Edge GetEdge(int edgeIndex)
        {
            if ((uint)edgeIndex >= (uint)_edges.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeIndex), edgeIndex, $"Must be in 0 .. {_edges.Length - 1}.");
            }

            return _edges[edgeIndex];
        }

        /// <summary>
        /// The indices (into <see cref="Edges"/>) of the edges incident to <paramref name="vertex"/>,
        /// in edge order.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public IReadOnlyList<int> IncidentEdges(int vertex)
        {
            if ((uint)vertex >= (uint)VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(vertex), vertex, $"Must be in 0 .. {VertexCount - 1}.");
            }

            return _incidentEdgesByVertex[vertex];
        }

        /// <summary>The number of edges incident to <paramref name="vertex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public int Degree(int vertex) => IncidentEdges(vertex).Count;

        /// <summary>
        /// Converts an edge index to the ZDD variable index that <see cref="Frontier.IDdSpec{TState}"/>
        /// implementations for this graph work with. This is the identity: edge index <c>i</c> is variable
        /// index <c>i</c>, kept as a named conversion rather than an implicit assumption.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        public int EdgeIndexToVariableIndex(int edgeIndex)
        {
            if ((uint)edgeIndex >= (uint)EdgeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeIndex), edgeIndex, $"Must be in 0 .. {EdgeCount - 1}.");
            }

            return edgeIndex;
        }

        /// <summary>The inverse of <see cref="EdgeIndexToVariableIndex"/> (also the identity).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        public int VariableIndexToEdgeIndex(int variableIndex)
        {
            if ((uint)variableIndex >= (uint)EdgeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(variableIndex), variableIndex, $"Must be in 0 .. {EdgeCount - 1}.");
            }

            return variableIndex;
        }

        /// <summary>
        /// Converts an edge index to the internal ZDD level (PLAN.md §4.1: <c>1</c> = leaf side,
        /// <c>EdgeCount</c> = root side). Edge <c>0</c> is decided first, at the root, so it maps to the
        /// highest level; edge <c>EdgeCount - 1</c> maps to level <c>1</c>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        public int EdgeIndexToLevel(int edgeIndex)
        {
            if ((uint)edgeIndex >= (uint)EdgeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeIndex), edgeIndex, $"Must be in 0 .. {EdgeCount - 1}.");
            }

            return EdgeCount - edgeIndex;
        }

        /// <summary>The inverse of <see cref="EdgeIndexToLevel"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is outside <c>1 .. EdgeCount</c>.</exception>
        public int LevelToEdgeIndex(int level)
        {
            if (level < 1 || level > EdgeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, $"Must be in 1 .. {EdgeCount}.");
            }

            return EdgeCount - level;
        }

        /// <summary>
        /// Returns a graph with the same vertices and edges, reordered by <paramref name="edgeOrder"/>.
        /// A placeholder for variable-order optimization: this constructor performs no reordering itself
        /// (that arrives in M3-1), it only lets a caller-supplied order be applied.
        /// </summary>
        /// <param name="edgeOrder">
        /// A permutation of <c>0 .. EdgeCount - 1</c>: the new graph's edge <c>i</c> is this graph's edge
        /// <c>edgeOrder[i]</c>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="edgeOrder"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="edgeOrder"/> is not a permutation of <c>0 .. EdgeCount - 1</c>.</exception>
        public Graph WithEdgeOrder(IReadOnlyList<int> edgeOrder)
        {
            ArgumentNullException.ThrowIfNull(edgeOrder);

            if (edgeOrder.Count != EdgeCount)
            {
                throw new ArgumentException($"Expected {EdgeCount} indices, got {edgeOrder.Count}.", nameof(edgeOrder));
            }

            var seen = new bool[EdgeCount];
            var reordered = new Edge[EdgeCount];
            for (int i = 0; i < EdgeCount; i++)
            {
                int source = edgeOrder[i];
                if ((uint)source >= (uint)EdgeCount || seen[source])
                {
                    throw new ArgumentException("Not a permutation of 0 .. EdgeCount - 1.", nameof(edgeOrder));
                }

                seen[source] = true;
                reordered[i] = _edges[source];
            }

            return new Graph(VertexCount, reordered);
        }

        /// <summary>Creates an <c>rows</c> × <c>cols</c> grid graph.</summary>
        /// <remarks>
        /// Vertex <c>(r, c)</c> is indexed <c>r * cols + c</c>. Edges are ordered row by row: each row's
        /// horizontal edges, then the vertical edges down to the next row, which is the layout the frontier
        /// method typically uses to keep the frontier narrow.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="rows"/> or <paramref name="cols"/> is not positive.</exception>
        public static Graph Grid(int rows, int cols)
        {
            if (rows <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), rows, "Must be positive.");
            }

            if (cols <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cols), cols, "Must be positive.");
            }

            var edges = new List<Edge>(rows * (cols - 1) + (rows - 1) * cols);

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int v = r * cols + c;
                    edges.Add(new Edge(v, v + 1));
                }

                if (r < rows - 1)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int v = r * cols + c;
                        edges.Add(new Edge(v, v + cols));
                    }
                }
            }

            return new Graph(rows * cols, edges);
        }

        /// <summary>Creates the complete graph on <paramref name="n"/> vertices (every pair of vertices joined).</summary>
        /// <remarks>Edges are ordered lexicographically by <c>(u, v)</c> with <c>u &lt; v</c>.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is not positive.</exception>
        public static Graph Complete(int n)
        {
            if (n <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(n), n, "Must be positive.");
            }

            var edges = new List<Edge>(n * (n - 1) / 2);
            for (int u = 0; u < n; u++)
            {
                for (int v = u + 1; v < n; v++)
                {
                    edges.Add(new Edge(u, v));
                }
            }

            return new Graph(n, edges);
        }

        /// <summary>Creates a simple cycle on <paramref name="n"/> vertices: <c>0-1-2-...-(n-1)-0</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is less than 3 (below that, the edge back to <c>0</c> would be a self-loop or a duplicate).</exception>
        public static Graph Cycle(int n)
        {
            if (n < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(n), n, "Must be at least 3.");
            }

            var edges = new List<Edge>(n);
            for (int i = 0; i < n; i++)
            {
                edges.Add(new Edge(i, (i + 1) % n));
            }

            return new Graph(n, edges);
        }

        /// <summary>Creates a simple path on <paramref name="n"/> vertices: <c>0-1-2-...-(n-1)</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is not positive.</exception>
        public static Graph Path(int n)
        {
            if (n <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(n), n, "Must be positive.");
            }

            var edges = new List<Edge>(Math.Max(0, n - 1));
            for (int i = 0; i < n - 1; i++)
            {
                edges.Add(new Edge(i, i + 1));
            }

            return new Graph(n, edges);
        }
    }
}

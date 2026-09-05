using System;
using System.Collections.Generic;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// The subset of <see cref="Graph"/> / <see cref="DirectedGraph"/> that <see cref="FrontierManager"/>
    /// and <see cref="EdgeOrdering"/> actually need: vertex/edge counts, each edge's endpoint pair (its
    /// direction, if it has one, discarded), and each vertex's incident edge list.
    /// </summary>
    /// <remarks>
    /// Frontier bookkeeping only ever asks "which vertices does this edge touch" and "which edges touch
    /// this vertex" — never which way an edge points — so both graph types can share this and everything
    /// built on it, rather than needing an undirected shadow graph (which would gain multi-edges from
    /// anti-parallel arcs and be rejected by <see cref="Graph"/>'s constructor). See
    /// docs/design/m7-directed-graphs.md §2.3.
    /// </remarks>
    internal sealed class EdgeTopology
    {
        private readonly (int U, int V)[] _endpoints;
        private readonly IReadOnlyList<int>[] _incidentByVertex;

        /// <summary>
        /// Wraps the given endpoint and incidence data. Both are taken over, not copied — callers are
        /// <see cref="Graph"/> and <see cref="DirectedGraph"/>, which already own this data and build it
        /// once in their own constructors.
        /// </summary>
        internal EdgeTopology(int vertexCount, (int U, int V)[] endpoints, IReadOnlyList<int>[] incidentByVertex)
        {
            VertexCount = vertexCount;
            _endpoints = endpoints;
            _incidentByVertex = incidentByVertex;
        }

        /// <summary>The number of vertices, indexed <c>0 .. VertexCount - 1</c>.</summary>
        public int VertexCount { get; }

        /// <summary>The number of edges.</summary>
        public int EdgeCount => _endpoints.Length;

        /// <summary>The endpoint pair of the edge at <paramref name="edgeIndex"/>, direction discarded.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        public (int U, int V) Endpoints(int edgeIndex)
        {
            if ((uint)edgeIndex >= (uint)_endpoints.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeIndex), edgeIndex, $"Must be in 0 .. {_endpoints.Length - 1}.");
            }

            return _endpoints[edgeIndex];
        }

        /// <summary>
        /// The indices of the edges incident to <paramref name="vertex"/> — for <see cref="DirectedGraph"/>,
        /// regardless of arc direction.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public IReadOnlyList<int> IncidentEdges(int vertex)
        {
            if ((uint)vertex >= (uint)VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(vertex), vertex, $"Must be in 0 .. {VertexCount - 1}.");
            }

            return _incidentByVertex[vertex];
        }

        /// <summary>The number of edges incident to <paramref name="vertex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public int Degree(int vertex) => IncidentEdges(vertex).Count;

        /// <summary>The endpoint of <paramref name="edgeIndex"/> that is not <paramref name="vertex"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="vertex"/> is neither endpoint of the edge.</exception>
        public int Other(int edgeIndex, int vertex)
        {
            (int u, int v) = Endpoints(edgeIndex);
            if (vertex == u)
            {
                return v;
            }

            if (vertex == v)
            {
                return u;
            }

            throw new ArgumentException($"Vertex {vertex} is not an endpoint of edge {edgeIndex}.", nameof(vertex));
        }
    }
}

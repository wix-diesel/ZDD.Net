using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of cliques of a graph: vertex sets in which every two vertices are adjacent.
    /// </summary>
    /// <remarks>
    /// <b>Implementation</b>: a clique of <c>graph</c> is exactly an independent set of <c>graph</c>'s
    /// complement (every pair that must be adjacent in the clique is exactly a pair that must
    /// <i>not</i> be adjacent — i.e. is an edge of the complement — for an independent set), so this spec
    /// is a thin wrapper around <see cref="IndependentSetSpec"/> built over the complement graph rather
    /// than a frontier walk of its own. <b>Variables are still <c>graph</c>'s vertices</b>, in the same
    /// ascending order <see cref="IndependentSetSpec"/> uses — the complement only changes which edges the
    /// frontier reasons about, not the vertex numbering.
    /// </remarks>
    public readonly struct CliqueSpec : IArrayDdSpec
    {
        private readonly IndependentSetSpec _independentSetSpec;

        /// <summary>Creates a spec for cliques of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public CliqueSpec(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            Graph = graph;
            _independentSetSpec = new IndependentSetSpec(Complement(graph));
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph { get; }

        /// <inheritdoc/>
        public int ArrayLength => _independentSetSpec.ArrayLength;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state) => _independentSetSpec.GetRoot(state);

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value) => _independentSetSpec.GetChild(state, level, value);

        /// <summary>The complement graph: same vertices, edges are exactly the non-edges of <paramref name="graph"/>.</summary>
        private static Graph Complement(Graph graph)
        {
            var present = new HashSet<Edge>();
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                present.Add(graph.GetEdge(i));
            }

            var complementEdges = new List<Edge>();
            for (int u = 0; u < graph.VertexCount; u++)
            {
                for (int v = u + 1; v < graph.VertexCount; v++)
                {
                    var edge = new Edge(u, v);
                    if (!present.Contains(edge))
                    {
                        complementEdges.Add(edge);
                    }
                }
            }

            return new Graph(graph.VertexCount, complementEdges);
        }
    }
}

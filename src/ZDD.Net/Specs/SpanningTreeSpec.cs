using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets that form a spanning tree of a graph: a connected, acyclic subgraph that
    /// touches every vertex.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: one <c>comp</c> code per frontier vertex, held in the state slot
    /// <see cref="FrontierManager.MateIndex"/> assigns it, plus one trailing slot counting how many
    /// connected components have closed so far. Where <see cref="PathSpec"/> tracks each frontier vertex's
    /// degree and chain partner via a <c>mate</c> array, this spec tracks which connected component each
    /// frontier vertex currently belongs to — see <see cref="SpanningComponentState"/> for the encoding and
    /// canonicalization (the component's representative is always the frontier slot with the smallest
    /// index among its current members).
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices as fresh singleton components, then — if the
    /// edge is taken — reject it if both endpoints are already in the same component (that would close a
    /// cycle), otherwise merge the two components. Finally, for each vertex this edge forgets: if
    /// forgetting it closes its component (no other frontier vertex belongs to it anymore) while edges
    /// still remain to be decided, reject — a component with no further incident edges can never merge
    /// with anything else, so the final edge set could never become a single tree spanning every vertex.
    /// A component is only allowed to close on the very last edge, and only once: a second component
    /// closing there too would mean two disjoint trees, not one.
    /// </para>
    /// </remarks>
    public readonly struct SpanningTreeSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;

        /// <summary>Creates a spec for spanning trees of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public SpanningTreeSpec(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _graph = graph;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>
        /// The closed-component-counter slot: one past the last comp slot, so it can never collide with a
        /// real frontier slot (those run <c>0 .. MaxFrontierSize - 1</c>).
        /// </summary>
        private int ClosedComponentCountSlot => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.VertexCount == 1)
            {
                return DdResult.True; // a single vertex is trivially its own spanning tree, with no edges
            }

            if (_graph.EdgeCount == 0)
            {
                return DdResult.False; // more than one vertex, no edges at all: can never be connected
            }

            for (int v = 0; v < _graph.VertexCount; v++)
            {
                if (_graph.Degree(v) == 0)
                {
                    return DdResult.False; // an isolated vertex can never join any tree
                }
            }

            // state is zero-filled by the caller: every comp slot already reads SpanningComponentState.SlotEmpty.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);
            int frontierLength = _frontierManager.MaxFrontierSize;

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                SpanningComponentState.Introduce(state, _frontierManager.MateIndex(edgeIndex, introducedVertices[i]));
            }

            if (value == 1)
            {
                int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                int sv = _frontierManager.MateIndex(edgeIndex, edge.V);
                if (!SpanningComponentState.TryMerge(state, frontierLength, su, sv))
                {
                    return DdResult.False;
                }
            }

            bool isFinalEdge = level == 1;

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                bool closed = SpanningComponentState.Forget(state, frontierLength, slot);
                if (!closed)
                {
                    continue;
                }

                if (!isFinalEdge)
                {
                    return DdResult.False; // closed too early: this component can never reach the rest of the graph
                }

                int closedCount = state[ClosedComponentCountSlot] + 1;
                if (closedCount > 1)
                {
                    return DdResult.False; // a second component finishing here means two trees, not one
                }

                state[ClosedComponentCountSlot] = closedCount;
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }
}

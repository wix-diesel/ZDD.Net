using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets whose kept edges form exactly <see cref="Target"/> connected components —
    /// Graphillion's <c>graphs(num_comps=...)</c>, one of the building blocks <see cref="Graphs.GraphConstraints"/>
    /// composes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Isolated vertices are not counted</b> — a vertex left with no selected incident edge, whether or
    /// not the graph itself gives it any edges to choose from, never contributes a component of its own.
    /// This differs from <see cref="ForestSpec"/>'s <see cref="ForestSpec.Components"/> (a <i>spanning</i>
    /// count: every vertex, isolated or not, is one of the forest's trees, since a forest must cover the
    /// whole vertex set) and matches Graphillion's <c>num_comps</c> instead, which only counts pieces with
    /// at least one edge. Concretely: <c>Target == 0</c> accepts exactly the empty edge set — any nonempty
    /// edge set puts at least one pair of vertices in a shared, edge-bearing component. This is the
    /// non-obvious half of the contract <see cref="Graphs.GraphConstraints.ComponentCount"/> documents.
    /// </para>
    /// <para>
    /// <b>State</b>: <see cref="ComponentCountComponentState"/>'s comp array (one code per frontier vertex,
    /// its sign recording whether the component has an edge yet) plus one trailing counter for how many
    /// edge-bearing components have closed so far. Merging two components already in the same one (closing a
    /// cycle) is never rejected — a cycle within one component is fine, exactly as in
    /// <see cref="GraphPartitionSpec"/>. A vertex with no incident edge at all in <see cref="Graph"/> never
    /// enters the frontier, so it never enters this accounting either — it is simply invisible, which is
    /// exactly the "ignore it" the remarks above describe.
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices as fresh singleton (no-edge-yet) components. If
    /// the edge is taken, merge the two endpoints' components, marking the result as edge-bearing. Finally,
    /// for each vertex this edge forgets: if forgetting it closes a component, and that component is
    /// edge-bearing, count it — rejecting outright once the closed count would exceed <see cref="Target"/>.
    /// At the last edge, the closed count must have reached exactly <see cref="Target"/>, not merely stayed
    /// at or under it.
    /// </para>
    /// </remarks>
    public readonly struct ComponentCountSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int _target;

        /// <summary>Creates a spec for edge sets of <paramref name="graph"/> with exactly <paramref name="target"/> connected components.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="target">
        /// The required number of components, not counting isolated vertices (see remarks); must be
        /// non-negative.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="target"/> is negative.</exception>
        public ComponentCountSpec(Graph graph, int target)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (target < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(target), target, "Must not be negative.");
            }

            _graph = graph;
            _target = target;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>The required number of components (isolated vertices excluded — see the type's remarks).</summary>
        public int Target => _target;

        /// <summary>
        /// The closed-component-counter slot: one past the last comp slot, so it can never collide with a
        /// real frontier slot (those run <c>0 .. MaxFrontierSize - 1</c>).
        /// </summary>
        private int ClosedCountSlot => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.EdgeCount == 0)
            {
                // The only member is the empty edge set, whose component count is 0 regardless of how
                // many isolated vertices the graph has (they are never counted — see the remarks).
                return _target == 0 ? DdResult.True : DdResult.False;
            }

            // state is zero-filled by the caller: every comp slot already reads ComponentCountComponentState.SlotEmpty.
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
                ComponentCountComponentState.Introduce(state, _frontierManager.MateIndex(edgeIndex, introducedVertices[i]));
            }

            if (value == 1)
            {
                int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                int sv = _frontierManager.MateIndex(edgeIndex, edge.V);
                ComponentCountComponentState.Merge(state, frontierLength, su, sv);
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                bool closed = ComponentCountComponentState.Forget(state, frontierLength, slot, out bool hadEdge);

                if (!closed || !hadEdge)
                {
                    continue;
                }

                int closedCount = state[ClosedCountSlot] + 1;
                if (closedCount > _target)
                {
                    return DdResult.False; // already too many components to ever hit the target
                }

                state[ClosedCountSlot] = closedCount;
            }

            int remaining = level - 1;
            if (remaining > 0)
            {
                return remaining;
            }

            return state[ClosedCountSlot] == _target ? DdResult.True : DdResult.False;
        }
    }
}

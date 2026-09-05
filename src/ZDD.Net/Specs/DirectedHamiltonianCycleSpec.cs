using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of arc sets that form a single directed simple cycle touching <b>every</b> vertex of the
    /// graph. The directed analogue of <see cref="HamiltonianCycleSpec"/> (docs/design/m7-directed-graphs.md §3.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <see cref="DirectedCycleSpec"/> with <see cref="DirectedCycleSpec.Single"/> pinned to
    /// <see langword="true"/> and one rule added on top, exactly as <see cref="HamiltonianCycleSpec"/> adds
    /// it to <see cref="CycleSpec"/>: a vertex must reach undirected degree 2 rather than being allowed to
    /// stay at 0 (<see cref="MateChainState.ForgetRequireVisited"/> in place of
    /// <see cref="MateChainState.ForgetAllowIsolated"/>).
    /// </para>
    /// <para>
    /// <b>No freshness tracking is needed here</b>, unlike <see cref="DirectedCycleSpec"/>: there, a
    /// still-fresh (2-vertex, anti-parallel-arc) chain closing must be rejected because the rest of the
    /// graph is allowed to stay untouched, so a lone digon could otherwise stand as a complete — but
    /// spurious — cycle family member. Here every vertex must reach degree 2 before the build finishes, so a
    /// digon closing early (with <see cref="Graph.VertexCount"/> &gt;= 3, guaranteed by the check below)
    /// always leaves at least one other vertex stuck at degree 0, which <see cref="MateChainState.ForgetRequireVisited"/>
    /// rejects on its own — the "every vertex visited" requirement already excludes it.
    /// </para>
    /// </remarks>
    public readonly struct DirectedHamiltonianCycleSpec : IArrayDdSpec
    {
        /// <summary>The vertex's one arc so far (undirected degree 1) points away from it.</summary>
        private const int DirectionOut = 0;

        /// <summary>The vertex's one arc so far (undirected degree 1) points into it.</summary>
        private const int DirectionIn = 1;

        private readonly DirectedGraph _graph;
        private readonly FrontierManager _frontierManager;

        /// <summary>Creates a spec for directed Hamiltonian cycles on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public DirectedHamiltonianCycleSpec(DirectedGraph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _graph = graph;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public DirectedGraph Graph => _graph;

        /// <summary>The direction-bit slot paired with mate slot <paramref name="mateSlot"/>.</summary>
        private int DirectionSlot(int mateSlot) => _frontierManager.MaxFrontierSize + mateSlot;

        /// <summary>
        /// The closed-cycle flag slot: one past the last direction slot, so it can never collide with a
        /// real frontier slot. Records whether the one allowed cycle has already closed, at which point
        /// every further arc is rejected.
        /// </summary>
        private int ClosedFlagSlot => 2 * _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => (2 * _frontierManager.MaxFrontierSize) + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            // A cycle needs at least 3 vertices; below that, the only closure available is the anti-parallel
            // pair's own 2-vertex digon, which docs/design/m7-directed-graphs.md §3.3's own acceptance test
            // (DirectedCycleSpec's ×2 relation to the undirected count) requires excluding.
            if (_graph.VertexCount < 3 || _graph.EdgeCount == 0)
            {
                return DdResult.False;
            }

            // Every vertex must reach in-degree 1 and out-degree 1: with either at 0 in the whole graph it
            // never can, so the entire family is empty regardless of which arcs are chosen.
            for (int v = 0; v < _graph.VertexCount; v++)
            {
                if (_graph.InDegree(v) == 0 || _graph.OutDegree(v) == 0)
                {
                    return DdResult.False;
                }
            }

            // state is zero-filled by the caller: every mate slot already reads SlotIsolated, every
            // direction slot reads DirectionOut, flag == 0.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            DirectedEdge arc = _graph.GetEdge(edgeIndex);

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, introducedVertices[i]);
                state[slot] = MateChainState.SlotIsolated;
                state[DirectionSlot(slot)] = DirectionOut;
            }

            if (value == 1 && !TakeArc(state, edgeIndex, arc))
            {
                return DdResult.False;
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                if (!MateChainState.ForgetRequireVisited(state, slot))
                {
                    return DdResult.False;
                }

                state[DirectionSlot(slot)] = DirectionOut;
            }

            int remaining = level - 1;
            if (remaining > 0)
            {
                return remaining;
            }

            return state[ClosedFlagSlot] == 1 ? DdResult.True : DdResult.False;
        }

        /// <summary>Splices the two endpoints of <paramref name="arc"/> together, or closes the cycle.</summary>
        /// <returns><see langword="false"/> if the connection is invalid: degree 3, one endpoint already
        /// owns an arc in the same direction, or any arc at all once the cycle has already closed.</returns>
        private bool TakeArc(Span<int> state, int edgeIndex, DirectedEdge arc)
        {
            if (state[ClosedFlagSlot] == 1)
            {
                return false; // the one allowed cycle has already closed
            }

            int u = arc.From;
            int v = arc.To;
            int su = _frontierManager.MateIndex(edgeIndex, u);
            int sv = _frontierManager.MateIndex(edgeIndex, v);
            int dirU = DirectionSlot(su);
            int dirV = DirectionSlot(sv);

            bool uHasDegree1 = state[su] != MateChainState.SlotIsolated && state[su] != MateChainState.SlotFixed;
            bool vHasDegree1 = state[sv] != MateChainState.SlotIsolated && state[sv] != MateChainState.SlotFixed;

            if (uHasDegree1 && state[dirU] == DirectionOut)
            {
                return false; // u already owns an outgoing arc
            }

            if (vHasDegree1 && state[dirV] == DirectionIn)
            {
                return false; // v already owns an incoming arc
            }

            MateChainState.SpliceResult result = MateChainState.Splice(state, su, sv);
            if (result == MateChainState.SpliceResult.Invalid)
            {
                return false;
            }

            if (result == MateChainState.SpliceResult.Closed)
            {
                state[ClosedFlagSlot] = 1;
            }

            state[dirU] = DirectionOut;
            state[dirV] = vHasDegree1 ? DirectionOut : DirectionIn;

            return true;
        }
    }
}

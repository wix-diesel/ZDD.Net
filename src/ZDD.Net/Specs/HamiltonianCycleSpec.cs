using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets that form a single simple cycle touching <b>every</b> vertex of the graph.
    /// </summary>
    /// <remarks>
    /// This is <see cref="CycleSpec"/> with <see cref="CycleSpec.Single"/> pinned to <see langword="true"/>
    /// and one rule added on top: a vertex must reach degree 2 rather than being allowed to stay at degree
    /// 0. <see cref="MateChainState.ForgetRequireVisited"/> in place of
    /// <see cref="MateChainState.ForgetAllowIsolated"/> is the entire difference from <see cref="CycleSpec"/>
    /// — forcing every vertex to close is what forces full vertex coverage, exactly as
    /// <see cref="HamiltonianPathSpec"/> forces it for its non-terminal vertices. Everything else — the
    /// mate-array encoding, the <see cref="MateChainState.Splice"/> call that accepts a closed chain as a
    /// completed cycle, and the trailing flag slot that both records the closure and rejects every edge
    /// taken after it — is identical to <see cref="CycleSpec"/>'s <see cref="CycleSpec.Single"/> mode.
    /// </remarks>
    public readonly struct HamiltonianCycleSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;

        /// <summary>Creates a spec for Hamiltonian cycles on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public HamiltonianCycleSpec(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _graph = graph;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>
        /// The closed-cycle flag slot: one past the last mate slot, so it can never collide with a real
        /// frontier slot (those run <c>0 .. MaxFrontierSize - 1</c>). Records whether the one allowed cycle
        /// has already closed, at which point every further edge is rejected.
        /// </summary>
        private int ClosedFlagSlot => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            // A cycle needs at least 3 vertices; below that, "every edge distinct, no self-loop" already
            // rules it out (Graph itself forbids self-loops and parallel edges).
            if (_graph.VertexCount < 3 || _graph.EdgeCount == 0)
            {
                return DdResult.False;
            }

            // Every vertex must reach degree 2: with fewer than two incident edges in the whole graph it
            // never can, so the entire family is empty regardless of which edges are chosen.
            for (int v = 0; v < _graph.VertexCount; v++)
            {
                if (_graph.Degree(v) < 2)
                {
                    return DdResult.False;
                }
            }

            // state is zero-filled by the caller: every mate slot already reads SlotIsolated, flag == 0.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                state[_frontierManager.MateIndex(edgeIndex, introducedVertices[i])] = MateChainState.SlotIsolated;
            }

            if (value == 1 && !TakeEdge(state, edgeIndex, edge))
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
            }

            int remaining = level - 1;
            if (remaining > 0)
            {
                return remaining;
            }

            return state[ClosedFlagSlot] == 1 ? DdResult.True : DdResult.False;
        }

        /// <summary>Splices the two endpoints of <paramref name="edge"/> together, or closes the cycle.</summary>
        /// <returns><see langword="false"/> if the connection is invalid: degree 3, or any edge at all once
        /// the cycle has already closed.</returns>
        private bool TakeEdge(Span<int> state, int edgeIndex, Edge edge)
        {
            if (state[ClosedFlagSlot] == 1)
            {
                return false; // the one allowed cycle has already closed
            }

            int su = _frontierManager.MateIndex(edgeIndex, edge.U);
            int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

            MateChainState.SpliceResult result = MateChainState.Splice(state, su, sv);
            if (result == MateChainState.SpliceResult.Invalid)
            {
                return false;
            }

            if (result == MateChainState.SpliceResult.Closed)
            {
                state[ClosedFlagSlot] = 1;
            }

            return true;
        }
    }
}

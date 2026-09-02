using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets that form one or more vertex-disjoint simple cycles: every vertex touched by
    /// the edge set has degree exactly 2, and (with <see cref="Single"/>) the edge set is required to be
    /// exactly one such cycle rather than a disjoint union of several. The empty edge set is never a member
    /// — a "cycle" always has at least one edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: one <c>mate</c> code per frontier vertex (see <see cref="MateChainState"/> for the
    /// encoding), held in the state slot <see cref="FrontierManager.MateIndex"/> assigns it, plus one
    /// trailing flag slot. <see cref="PathSpec"/> pins each chain's open ends at <c>s</c>/<c>t</c>; this
    /// spec instead lets a chain close back on itself — that closure is exactly what forms a cycle.
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices as <see cref="MateChainState.SlotIsolated"/>,
    /// then — if the edge is taken — <see cref="MateChainState.Splice"/> the two endpoints. Where
    /// <see cref="PathSpec"/> rejects a <see cref="MateChainState.SpliceResult.Closed"/> result outright,
    /// this spec accepts it: the chain has closed into a completed cycle. In <see cref="Single"/> mode the
    /// trailing flag records that closure and every subsequent edge is then rejected outright — a second
    /// cycle would violate "exactly one"; in the (default) multi-cycle mode the flag instead just records
    /// that some edge has been taken at all, so the empty edge set can be told apart from a real (possibly
    /// multi-component) cycle family at the end. Finally, for each vertex this edge forgets,
    /// <see cref="MateChainState.ForgetAllowIsolated"/> requires it to have ended at degree 0 (never
    /// touched) or degree 2 (a closed cycle's interior) — a degree-1 dead end can never become a valid
    /// cycle.
    /// </para>
    /// <para>
    /// Because <see cref="Single"/> mode is <see cref="CycleSpec"/> with strictly more restrictions applied
    /// on top of the same per-edge rules — nothing may be taken once a cycle has closed — every edge set it
    /// accepts is also accepted with <see cref="Single"/> off: the single-cycle family is always a subset
    /// of the multi-cycle family.
    /// </para>
    /// </remarks>
    public readonly struct CycleSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly bool _single;

        /// <summary>Creates a spec for simple cycles on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="single">
        /// When <see langword="true"/>, enumerates only edge sets that form exactly one simple cycle. When
        /// <see langword="false"/> (the default), enumerates every nonempty union of vertex-disjoint simple
        /// cycles.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public CycleSpec(Graph graph, bool single = false)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _graph = graph;
            _single = single;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>
        /// Whether the family is restricted to a single simple cycle, rather than any nonempty union of
        /// vertex-disjoint simple cycles.
        /// </summary>
        public bool Single => _single;

        /// <summary>
        /// The trailing flag slot: one past the last mate slot, so it can never collide with a real
        /// frontier slot (those run <c>0 .. MaxFrontierSize - 1</c>). In <see cref="Single"/> mode it
        /// records whether a cycle has already closed (blocking every further edge); otherwise it records
        /// whether any edge has been taken at all (excluding the empty edge set from the family).
        /// </summary>
        private int FlagSlot => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.EdgeCount == 0)
            {
                return DdResult.False;
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
                if (!MateChainState.ForgetAllowIsolated(state, slot))
                {
                    return DdResult.False;
                }
            }

            int remaining = level - 1;
            if (remaining > 0)
            {
                return remaining;
            }

            return state[FlagSlot] == 1 ? DdResult.True : DdResult.False;
        }

        /// <summary>Splices the two endpoints of <paramref name="edge"/> together, or closes a cycle.</summary>
        /// <returns><see langword="false"/> if the connection is invalid: degree 3, or — in <see cref="Single"/>
        /// mode — any edge at all once a cycle has already closed.</returns>
        private bool TakeEdge(Span<int> state, int edgeIndex, Edge edge)
        {
            if (_single && state[FlagSlot] == 1)
            {
                return false; // a cycle has already closed; "single" allows nothing further
            }

            int su = _frontierManager.MateIndex(edgeIndex, edge.U);
            int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

            MateChainState.SpliceResult result = MateChainState.Splice(state, su, sv);
            if (result == MateChainState.SpliceResult.Invalid)
            {
                return false;
            }

            if (_single)
            {
                if (result == MateChainState.SpliceResult.Closed)
                {
                    state[FlagSlot] = 1;
                }
            }
            else
            {
                state[FlagSlot] = 1; // some edge has been taken: the family excludes the empty edge set
            }

            return true;
        }
    }
}

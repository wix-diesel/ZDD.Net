using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of arc sets that form one or more vertex-disjoint directed simple cycles: every vertex
    /// touched by the arc set has in-degree = out-degree ∈ {0, 1}, and (with <see cref="Single"/>) the arc
    /// set is required to be exactly one such cycle. The directed analogue of <see cref="CycleSpec"/>
    /// (docs/design/m7-directed-graphs.md §3.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: <see cref="CycleSpec"/>'s mate array plus <see cref="DirectedPathSpec"/>'s one
    /// direction bit per frontier vertex, for the same reasons as <see cref="DirectedPathSpec"/> — but with
    /// one addition specific to closing a chain into a cycle: a "freshness" bit per frontier vertex, tracking
    /// whether the open chain it currently sits on has ever been extended past its original two vertices.
    /// </para>
    /// <para>
    /// <b>Why freshness is needed</b>: unlike <see cref="Graph"/>, <see cref="DirectedGraph"/> allows two
    /// distinct arcs directly between the same two vertices — the anti-parallel pair <c>u -&gt; v</c> /
    /// <c>v -&gt; u</c>. Taking both, back to back, closes <see cref="MateChainState.Splice"/>'s chain after
    /// exactly one edge on each side — a 2-vertex "digon" — which is not a cycle in the sense this spec (and
    /// the design's own acceptance test) means: <c>DirectedGraph.Bidirected(g)</c>'s directed simple cycle
    /// count must be exactly double <c>g</c>'s undirected simple cycle count (one count per orientation), and
    /// that only holds if a digon per edge is excluded (undirected simple graphs cannot have a length-2
    /// cycle at all, so allowing it here would add <c>EdgeCount</c> extra cycles the undirected side has no
    /// counterpart for). A closed chain must therefore have grown past its original pair before it may close.
    /// </para>
    /// <para>
    /// Freshness is tracked per current open end and kept in sync with <see cref="MateChainState.Splice"/>'s
    /// own branches: a brand-new two-isolated-vertex pair marks both ends fresh; any extension (an isolated
    /// vertex joining an existing open end) or merge (two existing chains joining) always marks both
    /// resulting open ends non-fresh, because either case grows the chain past two vertices. A
    /// <see cref="MateChainState.SpliceResult.Closed"/> result is accepted as a completed cycle only when at
    /// least one of the two closing endpoints is no longer fresh; a still-fresh pair is a digon and is
    /// rejected outright, mirroring how <see cref="DirectedPathSpec"/> rejects an out-of-direction arc reuse
    /// before ever calling <see cref="MateChainState.Splice"/>.
    /// </para>
    /// <para>
    /// Everything else — the trailing flag slot recording either "a cycle has already closed" (<see cref="Single"/>
    /// mode, blocking every further arc) or "some arc has been taken" (multi mode, excluding the empty arc
    /// set), and <see cref="MateChainState.ForgetAllowIsolated"/> for vertices leaving the frontier — is
    /// identical to <see cref="CycleSpec"/>.
    /// </para>
    /// </remarks>
    public readonly struct DirectedCycleSpec : IArrayDdSpec
    {
        /// <summary>The vertex's one arc so far (undirected degree 1) points away from it.</summary>
        private const int DirectionOut = 0;

        /// <summary>The vertex's one arc so far (undirected degree 1) points into it.</summary>
        private const int DirectionIn = 1;

        /// <summary>The open chain this vertex sits on has grown past its original two vertices.</summary>
        private const int ChainExtended = 0;

        /// <summary>The open chain this vertex sits on is still exactly its original two-vertex pair.</summary>
        private const int ChainFresh = 1;

        private readonly DirectedGraph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly bool _single;

        /// <summary>Creates a spec for directed simple cycles on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="single">
        /// When <see langword="true"/> (the default), enumerates only arc sets that form exactly one directed
        /// simple cycle. When <see langword="false"/>, enumerates every nonempty union of vertex-disjoint
        /// directed simple cycles.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public DirectedCycleSpec(DirectedGraph graph, bool single = true)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _graph = graph;
            _single = single;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public DirectedGraph Graph => _graph;

        /// <summary>
        /// Whether the family is restricted to a single directed simple cycle, rather than any nonempty
        /// union of vertex-disjoint directed simple cycles.
        /// </summary>
        public bool Single => _single;

        /// <summary>The direction-bit slot paired with mate slot <paramref name="mateSlot"/>.</summary>
        private int DirectionSlot(int mateSlot) => _frontierManager.MaxFrontierSize + mateSlot;

        /// <summary>The freshness-bit slot paired with mate slot <paramref name="mateSlot"/>.</summary>
        private int FreshSlot(int mateSlot) => (2 * _frontierManager.MaxFrontierSize) + mateSlot;

        /// <summary>
        /// The trailing flag slot: past every mate/direction/fresh slot triple, so it can never collide with
        /// a real frontier slot. In <see cref="Single"/> mode it records whether a cycle has already closed
        /// (blocking every further arc); otherwise it records whether any arc has been taken at all.
        /// </summary>
        private int FlagSlot => 3 * _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => (3 * _frontierManager.MaxFrontierSize) + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.EdgeCount == 0)
            {
                return DdResult.False;
            }

            // state is zero-filled by the caller: every mate slot already reads SlotIsolated, every
            // direction/fresh slot reads its 0-valued default (DirectionOut / ChainExtended), flag == 0.
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
                state[FreshSlot(slot)] = ChainExtended;
            }

            if (value == 1 && !TakeArc(state, edgeIndex, arc))
            {
                return DdResult.False;
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                if (!Forget(state, forgottenVertices[i], edgeIndex))
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

        /// <summary>Attempts to take arc <c>u -&gt; v</c>, splicing its endpoints and updating their bits.</summary>
        /// <returns>
        /// <see langword="false"/> if the arc cannot be taken: a cycle has already closed (<see cref="Single"/>
        /// mode only), one endpoint already owns an arc in the same direction, the connection would give an
        /// endpoint undirected degree 3, or it would close a still-fresh (2-vertex digon) chain.
        /// </returns>
        private bool TakeArc(Span<int> state, int edgeIndex, DirectedEdge arc)
        {
            if (_single && state[FlagSlot] == 1)
            {
                return false; // a cycle has already closed; "single" allows nothing further
            }

            int u = arc.From;
            int v = arc.To;
            int su = _frontierManager.MateIndex(edgeIndex, u);
            int sv = _frontierManager.MateIndex(edgeIndex, v);
            int dirU = DirectionSlot(su);
            int dirV = DirectionSlot(sv);

            int mu = state[su];
            int mv = state[sv];
            bool uHasDegree1 = mu != MateChainState.SlotIsolated && mu != MateChainState.SlotFixed;
            bool vHasDegree1 = mv != MateChainState.SlotIsolated && mv != MateChainState.SlotFixed;

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
                // See the class remarks: a still-fresh pair closing is a 2-vertex digon, not a cycle.
                if (state[FreshSlot(su)] == ChainFresh && state[FreshSlot(sv)] == ChainFresh)
                {
                    return false;
                }

                if (_single)
                {
                    state[FlagSlot] = 1;
                }
            }
            else
            {
                UpdateFreshness(state, su, sv, mu, mv);
            }

            if (!_single)
            {
                state[FlagSlot] = 1; // some arc has been taken: the family excludes the empty arc set
            }

            // u's undirected degree just became 1 (if it was isolated: this arc, outgoing, is its only one)
            // or 2 (if it already owned an arc: now fixed, direction bit never read again). Symmetric for v,
            // except a freshly opened v's one arc is incoming rather than outgoing — see DirectedPathSpec.TakeArc.
            state[dirU] = DirectionOut;
            state[dirV] = vHasDegree1 ? DirectionOut : DirectionIn;

            return true;
        }

        /// <summary>
        /// Updates the freshness bits of the chain's (possibly new) open ends after a non-closing
        /// <see cref="MateChainState.Splice"/>, mirroring its own branches exactly (see the class remarks).
        /// </summary>
        /// <param name="state">The state array.</param>
        /// <param name="su">The mate slot of one edge endpoint.</param>
        /// <param name="sv">The mate slot of the other edge endpoint.</param>
        /// <param name="mu">The mate code of slot <paramref name="su"/>, read before the <c>Splice</c> call.</param>
        /// <param name="mv">The mate code of slot <paramref name="sv"/>, read before the <c>Splice</c> call.</param>
        private void UpdateFreshness(Span<int> state, int su, int sv, int mu, int mv)
        {
            if (mu == MateChainState.SlotIsolated && mv == MateChainState.SlotIsolated)
            {
                // A brand-new two-vertex chain: su and sv are its (fresh) open ends.
                state[FreshSlot(su)] = ChainFresh;
                state[FreshSlot(sv)] = ChainFresh;
            }
            else if (mu == MateChainState.SlotIsolated)
            {
                // u extends v's chain; the new open ends are su and v's old far end (mv - 1).
                state[FreshSlot(su)] = ChainExtended;
                state[FreshSlot(mv - 1)] = ChainExtended;
            }
            else if (mv == MateChainState.SlotIsolated)
            {
                state[FreshSlot(sv)] = ChainExtended;
                state[FreshSlot(mu - 1)] = ChainExtended;
            }
            else
            {
                // Two existing chains merge; the new open ends are their two far ends.
                state[FreshSlot(mu - 1)] = ChainExtended;
                state[FreshSlot(mv - 1)] = ChainExtended;
            }
        }

        /// <summary>Validates and retires <paramref name="vertex"/>, which this arc forgets.</summary>
        /// <returns><see langword="false"/> if its final undirected degree is not 0 or 2.</returns>
        private bool Forget(Span<int> state, int vertex, int edgeIndex)
        {
            int slot = _frontierManager.MateIndex(edgeIndex, vertex);
            if (!MateChainState.ForgetAllowIsolated(state, slot))
            {
                return false;
            }

            // Clear so a reused slot never carries a stale direction/freshness code.
            state[DirectionSlot(slot)] = DirectionOut;
            state[FreshSlot(slot)] = ChainExtended;
            return true;
        }
    }
}

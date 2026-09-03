using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets <c>C</c> that are an <c>s</c>&#8211;<c>t</c> cut: removing <c>C</c> from the
    /// graph leaves <c>s</c> and <c>t</c> in different connected components. By default this is <i>every</i>
    /// such <c>C</c> (a large, upward-closed-ish family — any superset of a cut is again a cut); with
    /// <see cref="MinimalOnly"/> set, only the inclusion-minimal ones (no proper subset of <c>C</c> is
    /// itself a cut).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Convention</b>: a variable's value <c>1</c> means the edge is <i>in the cut</i> <c>C</c> (removed);
    /// <c>0</c> means it is kept. This is the opposite sense from <see cref="GraphPartitionSpec"/>, where
    /// <c>1</c> means an edge is kept — here the family being built is the cut sets themselves, so <c>1</c>
    /// has to mean "cut" for the ZDD's members to be exactly the answer.
    /// </para>
    /// <para>
    /// <b>State</b>: <see cref="CutComponentState"/>'s comp array (one code per frontier vertex) plus its
    /// parallel per-component side flag, marking whether a component currently holds <c>s</c>, <c>t</c>, or
    /// neither. <b>All cuts</b>: keeping an edge merges its endpoints' components, rejected outright only
    /// when that merge would join the <c>s</c>-side with the <c>t</c>-side (then <c>s</c>, <c>t</c> would
    /// stay connected, so this edge cannot be kept — it must be in <c>C</c>). Cutting an edge never touches
    /// the comp array. No other bookkeeping is needed: any assignment that never reconnects <c>s</c> and
    /// <c>t</c> is a valid cut, regardless of how many pieces the kept edges end up in.
    /// </para>
    /// <para>
    /// <b>Minimal cuts</b>: a cut is inclusion-minimal exactly when it is the edge boundary of a bipartition
    /// <c>(S, T)</c> of the vertices with <c>s &#8712; S</c>, <c>t &#8712; T</c>, where the graph restricted
    /// to each side is connected — cutting anything else is provably unnecessary. Three checks together
    /// enforce exactly that. First, precomputed once per vertex is whether it shares <c>s</c>'s original
    /// connected component; an edge entirely outside that component can never be part of a minimal cut, so
    /// cutting it is rejected outright (only keeping it is allowed). Second, inside that component, closing
    /// a comp-array component with <see cref="CutComponentState.FlagNone"/> — a fragment attached to neither
    /// side — is rejected: it would mean some cut edge around it was unnecessary. Third — the one the first
    /// two do not cover — <see cref="ParityComponentState"/> tracks, across <i>every</i> decided edge (kept
    /// or cut), whether the accumulated same/different requirements are even consistent; a decision that
    /// contradicts one already implied means <c>u</c> and <c>v</c> are certain to end up on the same side
    /// regardless (reachable via other kept edges), making this specific cut redundant, so it is rejected
    /// too. See that type's remarks for why this cannot be read off <see cref="CutComponentState"/> alone.
    /// </para>
    /// <para>
    /// <b>When <c>s</c> and <c>t</c> already sit in different original components</b> (e.g. either is
    /// isolated), every edge is "outside" <c>s</c>'s component by the definition above, so
    /// <see cref="MinimalOnly"/> forces every edge kept — the unique minimal cut is <c>&#8709;</c>, correctly:
    /// no edge is ever necessary to keep two already-disconnected vertices apart.
    /// </para>
    /// </remarks>
    public readonly struct CutSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int _s;
        private readonly int _t;
        private readonly bool _minimalOnly;
        private readonly bool[] _relevant;

        /// <summary>Creates a spec for <paramref name="s"/>&#8211;<paramref name="t"/> cuts of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="s">One terminal.</param>
        /// <param name="t">The other terminal.</param>
        /// <param name="minimalOnly">
        /// When <see langword="true"/>, enumerates only inclusion-minimal cuts; otherwise every cut.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="s"/> or <paramref name="t"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public CutSpec(Graph graph, int s, int t, bool minimalOnly = false)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if ((uint)s >= (uint)graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(s), s, $"Must be in 0 .. {graph.VertexCount - 1}.");
            }

            if ((uint)t >= (uint)graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(t), t, $"Must be in 0 .. {graph.VertexCount - 1}.");
            }

            _graph = graph;
            _s = s;
            _t = t;
            _minimalOnly = minimalOnly;
            _frontierManager = new FrontierManager(graph);
            _relevant = ComputeRelevant(graph, s, t);
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>One terminal.</summary>
        public int S => _s;

        /// <summary>The other terminal.</summary>
        public int T => _t;

        /// <summary>Whether only inclusion-minimal cuts are enumerated.</summary>
        public bool MinimalOnly => _minimalOnly;

        /// <summary>The number of comp slots — also the offset of the parallel flag array.</summary>
        private int FrontierLength => _frontierManager.MaxFrontierSize;

        /// <summary>
        /// The offset of the parity array (<see cref="ParityComponentState"/>), used only in
        /// <see cref="MinimalOnly"/> mode: one frontier length past the flag array.
        /// </summary>
        private int ParityOffset => 2 * _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => _minimalOnly ? 3 * _frontierManager.MaxFrontierSize : 2 * _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_s == _t)
            {
                return DdResult.False; // a vertex is never disconnected from itself; no edge set can do it
            }

            if (_graph.EdgeCount == 0)
            {
                return DdResult.True; // the only possible member, the empty edge set, is always a valid (and minimal) cut
            }

            // state is zero-filled by the caller: every comp slot already reads CutComponentState.SlotEmpty
            // and every flag already reads CutComponentState.FlagNone.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);
            int frontierLength = FrontierLength;

            // Sliced to its own zero-based region so ParityComponentState's internal scans (which run
            // 0 .. frontierLength - 1) never have to know about its offset within the shared state array.
            Span<int> parityState = _minimalOnly ? state.Slice(ParityOffset, frontierLength) : default;

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                int vertex = introducedVertices[i];
                int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                int flag = vertex == _s ? CutComponentState.FlagS : vertex == _t ? CutComponentState.FlagT : CutComponentState.FlagNone;
                CutComponentState.Introduce(state, frontierLength, slot, flag);

                if (_minimalOnly)
                {
                    ParityComponentState.Introduce(parityState, slot);
                }
            }

            int su = _frontierManager.MateIndex(edgeIndex, edge.U);
            int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

            if (value == 1)
            {
                if (_minimalOnly && !_relevant[edge.U])
                {
                    return DdResult.False; // this edge could never matter to the s-t separation: cutting it is not minimal
                }
            }
            else if (!CutComponentState.Merge(state, frontierLength, su, sv))
            {
                return DdResult.False; // keeping this edge would reconnect s and t
            }

            if (_minimalOnly && !ParityComponentState.Union(parityState, frontierLength, su, sv, mustBeSame: value == 0))
            {
                return DdResult.False; // u, v are already forced the opposite way by earlier decisions: this branch is not a consistent 2-coloring
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int vertex = forgottenVertices[i];
                int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                bool closed = CutComponentState.Forget(state, frontierLength, slot, out int flag);

                if (closed && _minimalOnly && _relevant[vertex] && flag == CutComponentState.FlagNone)
                {
                    return DdResult.False; // an unattached fragment inside s and t's shared component: an unnecessary cut
                }

                if (_minimalOnly)
                {
                    ParityComponentState.Forget(parityState, frontierLength, slot);
                }
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }

        /// <summary>
        /// Which vertices share <paramref name="s"/>'s connected component in the full, undirected
        /// <paramref name="graph"/> — but only when that component also holds <paramref name="t"/>; if
        /// <paramref name="s"/> and <paramref name="t"/> are already in different original components, no
        /// edge is ever relevant (<c>t</c> is unreachable regardless, so no edge is ever necessary to keep
        /// it that way, not even <paramref name="s"/>'s own component's edges).
        /// </summary>
        private static bool[] ComputeRelevant(Graph graph, int s, int t)
        {
            var parent = new int[graph.VertexCount];
            for (int v = 0; v < graph.VertexCount; v++)
            {
                parent[v] = v;
            }

            for (int i = 0; i < graph.EdgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);
                int ru = Find(parent, edge.U);
                int rv = Find(parent, edge.V);
                if (ru != rv)
                {
                    parent[ru] = rv;
                }
            }

            var relevant = new bool[graph.VertexCount];
            int sRoot = Find(parent, s);

            if (Find(parent, t) != sRoot)
            {
                return relevant; // s, t already disconnected: no edge is ever necessary, leave every entry false
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                relevant[v] = Find(parent, v) == sRoot;
            }

            return relevant;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }
    }
}

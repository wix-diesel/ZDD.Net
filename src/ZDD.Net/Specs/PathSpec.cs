using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets that form a simple <c>s</c>–<c>t</c> path (Knuth's <c>SIMPATH</c>,
    /// TAOCP Vol.4A §7.1.4), or, with <see cref="AllowAnyEndpoints"/>, every simple path regardless
    /// of which two vertices it connects.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: one <c>mate</c> code per frontier vertex (see <see cref="MateChainState"/> for the
    /// encoding), held in the state slot <see cref="FrontierManager.MateIndex"/> assigns it, plus (only
    /// meaningful when <see cref="AllowAnyEndpoints"/> is set) one extra trailing slot counting how many
    /// vertices have already been forgotten as a path endpoint.
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices as <see cref="MateChainState.SlotIsolated"/>,
    /// then — if the edge is taken — <see cref="MateChainState.Splice"/> the two endpoints, rejecting
    /// outright a degree-3 attempt or a connection that would close a cycle (the two endpoints are already
    /// the two ends of the same partial-path piece — invalid for a path, unlike <see cref="CycleSpec"/>).
    /// Finally, for each vertex this edge forgets: a non-terminal vertex must be
    /// <see cref="MateChainState.SlotIsolated"/> or <see cref="MateChainState.SlotFixed"/> (a degree-1 dead
    /// end is not a valid path interior), while <c>s</c>/<c>t</c> (fixed mode) must be exactly degree 1; in
    /// <see cref="AllowAnyEndpoints"/> mode any vertex may end at degree 1, capped at two such vertices
    /// overall via the trailing counter slot. A forgotten slot is always reset to
    /// <see cref="MateChainState.SlotIsolated"/> — leaving a stale code behind would keep otherwise-identical
    /// states from merging (see <see cref="IArrayDdSpec"/>'s remark on clearing slots that no longer
    /// matter), splitting the frontier for no semantic reason.
    /// </para>
    /// <para>
    /// Because every non-terminal vertex is forced to close at degree 2 before it can be forgotten, a
    /// partial-path piece can only ever end at a vertex allowed to stay open — in fixed mode, that is only
    /// <c>s</c> or <c>t</c>, so a chain leaving <c>s</c> is forced (on pain of rejection) to arrive at
    /// <c>t</c> and nowhere else; no separate "did the two ends actually meet" check is needed.
    /// </para>
    /// </remarks>
    public readonly struct PathSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int _s;
        private readonly int _t;
        private readonly bool _allowAnyEndpoints;

        /// <summary>Creates a spec for simple paths on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="s">One endpoint. Ignored when <paramref name="allowAnyEndpoints"/> is <see langword="true"/>.</param>
        /// <param name="t">The other endpoint. Ignored when <paramref name="allowAnyEndpoints"/> is <see langword="true"/>.</param>
        /// <param name="allowAnyEndpoints">
        /// When <see langword="true"/>, enumerates every simple path in the graph (any pair of distinct
        /// vertices as endpoints) instead of only <paramref name="s"/>–<paramref name="t"/> paths.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="s"/> or <paramref name="t"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public PathSpec(Graph graph, int s, int t, bool allowAnyEndpoints = false)
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
            _allowAnyEndpoints = allowAnyEndpoints;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>One endpoint, when <see cref="AllowAnyEndpoints"/> is <see langword="false"/>.</summary>
        public int S => _s;

        /// <summary>The other endpoint, when <see cref="AllowAnyEndpoints"/> is <see langword="false"/>.</summary>
        public int T => _t;

        /// <summary>Whether every simple path is enumerated, rather than only <see cref="S"/>–<see cref="T"/> paths.</summary>
        public bool AllowAnyEndpoints => _allowAnyEndpoints;

        /// <summary>
        /// The endpoint-counter slot used only in <see cref="AllowAnyEndpoints"/> mode: one past the last
        /// mate slot, so it can never collide with a real frontier slot (those run <c>0 .. MaxFrontierSize - 1</c>).
        /// </summary>
        private int CounterSlot => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.EdgeCount == 0)
            {
                return DdResult.False;
            }

            if (!_allowAnyEndpoints)
            {
                // s == t: no simple path (of at least one edge) starts and ends at the same vertex.
                // A zero-degree terminal could never be forgotten at degree 1, so it can never be
                // satisfied by the per-edge checks below and must be rejected up front instead.
                if (_s == _t || _graph.Degree(_s) == 0 || _graph.Degree(_t) == 0)
                {
                    return DdResult.False;
                }
            }

            // state is zero-filled by the caller: every slot already reads SlotIsolated / counter == 0.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);

            // Indexed access rather than foreach: IntroducedVertices/ForgottenVertices are typed as
            // IReadOnlyList<int>, and foreach against an interface type boxes ReadOnlyCollection<T>'s
            // enumerator on every call — indexing through Count/this[] allocates nothing.
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
                if (!Forget(state, edgeIndex, forgottenVertices[i]))
                {
                    return DdResult.False;
                }
            }

            int remaining = level - 1;
            if (remaining > 0)
            {
                return remaining;
            }

            if (_allowAnyEndpoints)
            {
                return state[CounterSlot] == 2 ? DdResult.True : DdResult.False;
            }

            return DdResult.True;
        }

        /// <summary>Splices the two endpoints of <paramref name="edge"/> together.</summary>
        /// <returns><see langword="false"/> if the connection is invalid (degree 3, or it would close a cycle).</returns>
        private bool TakeEdge(Span<int> state, int edgeIndex, Edge edge)
        {
            int su = _frontierManager.MateIndex(edgeIndex, edge.U);
            int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

            // A path can never accept the Closed outcome: closing a chain into a cycle is invalid here
            // (unlike CycleSpec, which is built on the very same Splice).
            return MateChainState.Splice(state, su, sv) == MateChainState.SpliceResult.Spliced;
        }

        /// <summary>Validates and retires <paramref name="vertex"/>, which this edge forgets.</summary>
        /// <returns><see langword="false"/> if its final degree makes the family it belongs to invalid.</returns>
        private bool Forget(Span<int> state, int edgeIndex, int vertex)
        {
            int slot = _frontierManager.MateIndex(edgeIndex, vertex);

            if (!_allowAnyEndpoints)
            {
                bool isTerminal = vertex == _s || vertex == _t;
                return isTerminal
                    ? MateChainState.ForgetTerminal(state, slot)
                    : MateChainState.ForgetAllowIsolated(state, slot);
            }

            int mate = state[slot];
            if (mate != MateChainState.SlotIsolated && mate != MateChainState.SlotFixed)
            {
                int counter = state[CounterSlot] + 1;
                if (counter > 2)
                {
                    return false; // a simple path has exactly two endpoints
                }

                state[CounterSlot] = counter;
            }

            if (mate >= 1)
            {
                state[mate - 1] = MateChainState.SlotEndpointDone;
            }

            state[slot] = MateChainState.SlotIsolated; // clear so a reused slot never carries a stale, merge-blocking code
            return true;
        }
    }
}

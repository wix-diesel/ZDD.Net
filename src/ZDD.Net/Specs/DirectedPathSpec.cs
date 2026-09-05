using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of arc sets that form a directed simple <c>from</c>–<c>to</c> path, or, with
    /// <see cref="AllowAnyEndpoints"/>, every directed simple path regardless of which two vertices it
    /// connects. The directed analogue of <see cref="PathSpec"/> (docs/design/m7-directed-graphs.md §3.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: a directed constraint decomposes into "shape as an undirected graph" (connectivity,
    /// absence of a cycle) plus "in/out-degree per vertex". The former is exactly what the existing mate
    /// array (<see cref="MateChainState"/>) already tracks, unmodified. What is new is one bit per
    /// frontier vertex: while its undirected degree is 1, whether that one arc points in or out — once it
    /// reaches undirected degree 2, a simple path forces exactly one in and one out, so no further
    /// bookkeeping is needed. This spec stores that bit as a whole packed slot (one per frontier vertex,
    /// appended after the mate slots) rather than actually packing to the bit level: <see cref="IArrayDdSpec"/>
    /// already packs each slot to as few bytes as its value range needs, so a two-valued slot costs one
    /// byte on its own. Repacking multiple vertices' bits into a single slot would save that further, but
    /// only pays off once state size is shown to bottleneck a build — premature here.
    /// </para>
    /// <para>
    /// <b>Per arc <c>u -&gt; v</c>, when taken</b>: rejected outright if <c>u == To</c> (<see cref="To"/> has
    /// no outgoing arc) or <c>v == From</c> (<see cref="From"/> has no incoming arc); rejected if <c>u</c>
    /// already owns an outgoing arc or <c>v</c> already owns an incoming arc (both read off the direction
    /// bit, only meaningful while the vertex's undirected degree is 1); then <see cref="MateChainState.Splice"/>
    /// runs exactly as it does for <see cref="PathSpec"/>, rejecting a degree-3 attempt or a connection that
    /// would close a cycle. Finally each endpoint's direction bit is updated: freshly opened (was undirected
    /// degree 0) it is set to the real direction of this arc; freshly closed (was already undirected degree
    /// 1, now 2) it is reset to a fixed sentinel value, since it is never read again for that vertex — done
    /// unconditionally so two histories that reach the same mate-array content by different bit sequences
    /// still compare equal (see <see cref="IArrayDdSpec"/>'s remark on clearing slots that no longer matter).
    /// </para>
    /// <para>
    /// <b>Per vertex <c>w</c>, when it leaves the frontier</b>: in fixed-endpoint mode, <see cref="From"/>
    /// must end at (in 0, out 1) and <see cref="To"/> at (in 1, out 0); every other vertex must end at
    /// (0, 0) or (1, 1) — an undirected degree-1 dead end is never valid, matching <see cref="PathSpec"/>'s
    /// non-terminal rule. In <see cref="AllowAnyEndpoints"/> mode there is no fixed <see cref="From"/>/<see cref="To"/>
    /// restriction on arcs; instead exactly one vertex overall may finish as a pure source (0, 1) and exactly
    /// one as a pure sink (1, 0), tracked with two trailing one-shot counter slots (the directed counterpart
    /// of <see cref="PathSpec.AllowAnyEndpoints"/>'s single counter, split in two because direction now
    /// distinguishes which kind of endpoint a degree-1 vertex is).
    /// </para>
    /// </remarks>
    public readonly struct DirectedPathSpec : IArrayDdSpec
    {
        /// <summary>The vertex's one arc so far (undirected degree 1) points away from it.</summary>
        private const int DirectionOut = 0;

        /// <summary>The vertex's one arc so far (undirected degree 1) points into it.</summary>
        private const int DirectionIn = 1;

        private readonly DirectedGraph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int _from;
        private readonly int _to;
        private readonly bool _allowAnyEndpoints;

        /// <summary>Creates a spec for directed simple paths on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="from">The source endpoint. Ignored when <paramref name="allowAnyEndpoints"/> is <see langword="true"/>.</param>
        /// <param name="to">The sink endpoint. Ignored when <paramref name="allowAnyEndpoints"/> is <see langword="true"/>.</param>
        /// <param name="allowAnyEndpoints">
        /// When <see langword="true"/>, enumerates every directed simple path in the graph (any ordered
        /// pair of distinct vertices as source/sink) instead of only <paramref name="from"/>–<paramref name="to"/> paths.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="from"/> or <paramref name="to"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public DirectedPathSpec(DirectedGraph graph, int from, int to, bool allowAnyEndpoints = false)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if ((uint)from >= (uint)graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(from), from, $"Must be in 0 .. {graph.VertexCount - 1}.");
            }

            if ((uint)to >= (uint)graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(to), to, $"Must be in 0 .. {graph.VertexCount - 1}.");
            }

            _graph = graph;
            _from = from;
            _to = to;
            _allowAnyEndpoints = allowAnyEndpoints;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public DirectedGraph Graph => _graph;

        /// <summary>The source endpoint, when <see cref="AllowAnyEndpoints"/> is <see langword="false"/>.</summary>
        public int From => _from;

        /// <summary>The sink endpoint, when <see cref="AllowAnyEndpoints"/> is <see langword="false"/>.</summary>
        public int To => _to;

        /// <summary>Whether every directed simple path is enumerated, rather than only <see cref="From"/>–<see cref="To"/> paths.</summary>
        public bool AllowAnyEndpoints => _allowAnyEndpoints;

        /// <summary>The direction-bit slot paired with mate slot <paramref name="mateSlot"/>.</summary>
        private int DirectionSlot(int mateSlot) => _frontierManager.MaxFrontierSize + mateSlot;

        /// <summary>
        /// The one-shot "have we already forgotten a pure-source vertex" slot used only in
        /// <see cref="AllowAnyEndpoints"/> mode: past every mate/direction slot pair, so it can never
        /// collide with a real frontier slot.
        /// </summary>
        private int SourceSeenSlot => 2 * _frontierManager.MaxFrontierSize;

        /// <summary>The equivalent one-shot slot for a pure-sink vertex; see <see cref="SourceSeenSlot"/>.</summary>
        private int SinkSeenSlot => (2 * _frontierManager.MaxFrontierSize) + 1;

        /// <inheritdoc/>
        public int ArrayLength => (2 * _frontierManager.MaxFrontierSize) + 2;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.EdgeCount == 0)
            {
                return DdResult.False;
            }

            if (!_allowAnyEndpoints)
            {
                // from == to: no directed simple path (of at least one arc) starts and ends at the same
                // vertex. A source with no outgoing arc, or a sink with no incoming arc, could never reach
                // the (out 1, in 0) / (in 1, out 0) degree the per-vertex checks below require.
                if (_from == _to || _graph.OutDegree(_from) == 0 || _graph.InDegree(_to) == 0)
                {
                    return DdResult.False;
                }
            }

            // state is zero-filled by the caller: every mate/direction slot already reads
            // SlotIsolated / DirectionOut, and both counters read 0.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            DirectedEdge arc = _graph.GetEdge(edgeIndex);

            // Indexed access rather than foreach: IntroducedVertices/ForgottenVertices are typed as
            // IReadOnlyList<int>, and foreach against an interface type boxes ReadOnlyCollection<T>'s
            // enumerator on every call — indexing through Count/this[] allocates nothing (see PathSpec.GetChild).
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
                return state[SourceSeenSlot] == 1 && state[SinkSeenSlot] == 1 ? DdResult.True : DdResult.False;
            }

            return DdResult.True;
        }

        /// <summary>Attempts to take arc <c>u -&gt; v</c>, splicing its endpoints and updating their direction bits.</summary>
        /// <returns>
        /// <see langword="false"/> if the arc cannot be taken: it leaves <see cref="To"/> or enters
        /// <see cref="From"/> (fixed-endpoint mode only), one endpoint already owns an arc in the same
        /// direction, the connection would give an endpoint undirected degree 3, or it would close a cycle.
        /// </returns>
        private bool TakeArc(Span<int> state, int edgeIndex, DirectedEdge arc)
        {
            int u = arc.From;
            int v = arc.To;

            if (!_allowAnyEndpoints && (u == _to || v == _from))
            {
                return false;
            }

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

            if (MateChainState.Splice(state, su, sv) != MateChainState.SpliceResult.Spliced)
            {
                return false; // degree 3 at one endpoint, or this arc would close a directed cycle
            }

            // u's undirected degree just became 1 (if it was isolated: this arc, outgoing, is its only
            // one) or 2 (if it already owned an arc: now fixed, so the direction bit is never read again
            // and settles to the same sentinel value either way — both cases happen to be DirectionOut).
            state[dirU] = DirectionOut;

            // Symmetric for v, except a freshly opened v's one arc is incoming rather than outgoing, so
            // the two cases no longer coincide and must be told apart explicitly.
            state[dirV] = vHasDegree1 ? DirectionOut : DirectionIn;

            return true;
        }

        /// <summary>Validates and retires <paramref name="vertex"/>, which this arc forgets.</summary>
        /// <returns><see langword="false"/> if its final in/out-degree makes the family it belongs to invalid.</returns>
        private bool Forget(Span<int> state, int edgeIndex, int vertex)
        {
            int slot = _frontierManager.MateIndex(edgeIndex, vertex);
            int dirSlot = DirectionSlot(slot);
            int mate = state[slot];
            bool hasDegree1 = mate != MateChainState.SlotIsolated && mate != MateChainState.SlotFixed;

            if (!_allowAnyEndpoints)
            {
                bool ok;
                if (vertex == _from)
                {
                    ok = hasDegree1 && state[dirSlot] == DirectionOut; // must end at (out 1, in 0)
                }
                else if (vertex == _to)
                {
                    ok = hasDegree1 && state[dirSlot] == DirectionIn; // must end at (in 1, out 0)
                }
                else
                {
                    ok = !hasDegree1; // a directed dead end (in/out 1) is never a valid path interior
                }

                if (!ok)
                {
                    return false;
                }
            }
            else if (hasDegree1)
            {
                int counterSlot = state[dirSlot] == DirectionOut ? SourceSeenSlot : SinkSeenSlot;
                if (state[counterSlot] != 0)
                {
                    return false; // a directed simple path has exactly one source and one sink
                }

                state[counterSlot] = 1;
            }

            if (mate >= 1)
            {
                state[mate - 1] = MateChainState.SlotEndpointDone;
            }

            // Clear both so a reused slot never carries a stale, merge-blocking code (see IArrayDdSpec's
            // remark on clearing slots that no longer matter).
            state[slot] = MateChainState.SlotIsolated;
            state[dirSlot] = DirectionOut;
            return true;
        }
    }
}

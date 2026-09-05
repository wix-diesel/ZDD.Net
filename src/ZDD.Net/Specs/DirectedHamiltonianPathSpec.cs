using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of arc sets that form a directed simple <c>s</c>–<c>t</c> path touching <b>every</b>
    /// vertex of the graph — the directed traveling salesman problem. The directed analogue of
    /// <see cref="HamiltonianPathSpec"/> (docs/design/m7-directed-graphs.md §3.3).
    /// </summary>
    /// <remarks>
    /// This is <see cref="DirectedPathSpec"/> in fixed <c>s</c>/<c>t</c> mode with one rule added, exactly as
    /// <see cref="HamiltonianPathSpec"/> adds it to <see cref="PathSpec"/>: a non-terminal vertex must reach
    /// undirected degree 2 (in 1, out 1) rather than being allowed to stay at 0. No freshness/digon tracking
    /// is needed (contrast <see cref="DirectedCycleSpec"/>): a path never closes a chain at all — every
    /// <see cref="MateChainState.SpliceResult.Closed"/> result is rejected outright, digon or not — so the
    /// only way to satisfy every vertex is genuine full coverage by one open chain.
    /// </remarks>
    public readonly struct DirectedHamiltonianPathSpec : IArrayDdSpec
    {
        /// <summary>The vertex's one arc so far (undirected degree 1) points away from it.</summary>
        private const int DirectionOut = 0;

        /// <summary>The vertex's one arc so far (undirected degree 1) points into it.</summary>
        private const int DirectionIn = 1;

        private readonly DirectedGraph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int _s;
        private readonly int _t;

        /// <summary>Creates a spec for directed Hamiltonian <paramref name="s"/>–<paramref name="t"/> paths on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="s">The source endpoint.</param>
        /// <param name="t">The sink endpoint.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="s"/> or <paramref name="t"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public DirectedHamiltonianPathSpec(DirectedGraph graph, int s, int t)
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
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public DirectedGraph Graph => _graph;

        /// <summary>The source endpoint.</summary>
        public int S => _s;

        /// <summary>The sink endpoint.</summary>
        public int T => _t;

        /// <summary>The direction-bit slot paired with mate slot <paramref name="mateSlot"/>.</summary>
        private int DirectionSlot(int mateSlot) => _frontierManager.MaxFrontierSize + mateSlot;

        /// <inheritdoc/>
        public int ArrayLength => 2 * _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.EdgeCount == 0)
            {
                return DdResult.False;
            }

            // s == t: no directed simple path (of at least one arc) starts and ends at the same vertex.
            if (_s == _t)
            {
                return DdResult.False;
            }

            if (_graph.OutDegree(_s) == 0 || _graph.InDegree(_t) == 0)
            {
                return DdResult.False;
            }

            // A non-terminal vertex must reach in-degree 1 and out-degree 1: with either at 0 in the whole
            // graph it never can, so the entire family is empty regardless of which arcs are chosen.
            for (int v = 0; v < _graph.VertexCount; v++)
            {
                if (v == _s || v == _t)
                {
                    continue;
                }

                if (_graph.InDegree(v) == 0 || _graph.OutDegree(v) == 0)
                {
                    return DdResult.False;
                }
            }

            // state is zero-filled by the caller: every mate/direction slot already reads
            // SlotIsolated / DirectionOut.
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
                if (!Forget(state, edgeIndex, forgottenVertices[i]))
                {
                    return DdResult.False;
                }
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }

        /// <summary>Attempts to take arc <c>u -&gt; v</c>, splicing its endpoints and updating their direction bits.</summary>
        /// <returns>
        /// <see langword="false"/> if the arc cannot be taken: it leaves <see cref="T"/> or enters
        /// <see cref="S"/>, one endpoint already owns an arc in the same direction, the connection would give
        /// an endpoint undirected degree 3, or it would close a cycle (a Hamiltonian path is still a path).
        /// </returns>
        private bool TakeArc(Span<int> state, int edgeIndex, DirectedEdge arc)
        {
            int u = arc.From;
            int v = arc.To;

            if (u == _t || v == _s)
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
                return false; // degree 3 at one endpoint, or this arc would close a cycle
            }

            state[dirU] = DirectionOut;
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

            bool ok;
            if (vertex == _s)
            {
                ok = hasDegree1 && state[dirSlot] == DirectionOut; // must end at (out 1, in 0)
            }
            else if (vertex == _t)
            {
                ok = hasDegree1 && state[dirSlot] == DirectionIn; // must end at (in 1, out 0)
            }
            else
            {
                ok = mate == MateChainState.SlotFixed; // every non-terminal vertex must be visited (degree 2)
            }

            if (!ok)
            {
                return false;
            }

            if (mate >= 1)
            {
                state[mate - 1] = MateChainState.SlotEndpointDone;
            }

            // Clear both so a reused slot never carries a stale, merge-blocking code.
            state[slot] = MateChainState.SlotIsolated;
            state[dirSlot] = DirectionOut;
            return true;
        }
    }
}

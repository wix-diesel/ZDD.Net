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
    /// <b>State</b>: one <c>mate</c> code per frontier vertex, held in the state slot
    /// <see cref="FrontierManager.MateIndex"/> assigns it, plus (only meaningful when
    /// <see cref="AllowAnyEndpoints"/> is set) one extra trailing slot counting how many vertices have
    /// already been forgotten as a path endpoint. A code is one of:
    /// </para>
    /// <list type="bullet">
    /// <item><description><see cref="SlotIsolated"/> (<c>0</c>): the vertex has degree 0 so far.</description></item>
    /// <item><description><see cref="SlotFixed"/> (<c>-1</c>): the vertex already has degree 2 — done, interior to the path.</description></item>
    /// <item><description><see cref="SlotEndpointDone"/> (<c>-2</c>): the vertex has degree 1, and the *other* end of its
    /// partial-path piece has already been forgotten as a finished endpoint (fixed <c>s</c>/<c>t</c> mode: that
    /// end is necessarily the other terminal, forced by the checks below — no need to remember which).</description></item>
    /// <item><description>Any other value <c>k &gt;= 1</c>: the vertex has degree 1, and the other end of its
    /// partial-path piece is the vertex currently occupying frontier slot <c>k - 1</c>.</description></item>
    /// </list>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices as <see cref="SlotIsolated"/>, then — if the edge
    /// is taken — reject a vertex already at <see cref="SlotFixed"/> (would make degree 3) or a connection
    /// that would close a cycle (the two endpoints are already the two ends of the same partial-path piece),
    /// otherwise splice the two mate chains together. Finally, for each vertex this edge forgets: a non-terminal
    /// vertex must be <see cref="SlotIsolated"/> or <see cref="SlotFixed"/> (a degree-1 dead end is not a valid
    /// path interior), while <c>s</c>/<c>t</c> (fixed mode) must be exactly degree 1; in
    /// <see cref="AllowAnyEndpoints"/> mode any vertex may end at degree 1, capped at two such vertices overall
    /// via the trailing counter slot. A forgotten slot is always reset to <see cref="SlotIsolated"/> — leaving
    /// a stale code behind would keep otherwise-identical states from merging (see
    /// <see cref="IArrayDdSpec"/>'s remark on clearing slots that no longer matter), splitting the frontier
    /// for no semantic reason.
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
        /// <summary>The vertex has degree 0 so far.</summary>
        private const int SlotIsolated = 0;

        /// <summary>The vertex already has degree 2: interior to a path, no further edge may touch it.</summary>
        private const int SlotFixed = -1;

        /// <summary>The vertex has degree 1, and the other end of its chain is already a finished endpoint.</summary>
        private const int SlotEndpointDone = -2;

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
                state[_frontierManager.MateIndex(edgeIndex, introducedVertices[i])] = SlotIsolated;
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
            int mu = state[su];
            int mv = state[sv];

            if (mu == SlotFixed || mv == SlotFixed)
            {
                return false; // would give one endpoint degree 3
            }

            if ((mu >= 1 && mu - 1 == sv) || (mv >= 1 && mv - 1 == su))
            {
                return false; // the two endpoints already share a chain: this edge would close a cycle
            }

            if (mu == SlotIsolated && mv == SlotIsolated)
            {
                // A brand-new two-vertex chain: u and v become each other's mate.
                state[su] = sv + 1;
                state[sv] = su + 1;
            }
            else if (mu == SlotIsolated)
            {
                // u extends v's chain; v becomes interior, u inherits v's old far end.
                state[su] = mv;
                state[sv] = SlotFixed;
                if (mv >= 1)
                {
                    state[mv - 1] = su + 1;
                }
            }
            else if (mv == SlotIsolated)
            {
                state[sv] = mu;
                state[su] = SlotFixed;
                if (mu >= 1)
                {
                    state[mu - 1] = sv + 1;
                }
            }
            else
            {
                // Two existing chains merge through u and v, which both become interior; their far
                // ends now point at each other (or, if either was already a finished endpoint, the
                // whole path is complete and there is nothing left to redirect).
                state[su] = SlotFixed;
                state[sv] = SlotFixed;
                if (mu >= 1)
                {
                    state[mu - 1] = mv;
                }

                if (mv >= 1)
                {
                    state[mv - 1] = mu;
                }
            }

            return true;
        }

        /// <summary>Validates and retires <paramref name="vertex"/>, which this edge forgets.</summary>
        /// <returns><see langword="false"/> if its final degree makes the family it belongs to invalid.</returns>
        private bool Forget(Span<int> state, int edgeIndex, int vertex)
        {
            int slot = _frontierManager.MateIndex(edgeIndex, vertex);
            int mate = state[slot];

            if (!_allowAnyEndpoints)
            {
                bool isTerminal = vertex == _s || vertex == _t;
                if (isTerminal)
                {
                    if (mate == SlotIsolated || mate == SlotFixed)
                    {
                        return false; // s/t must end the build at degree exactly 1
                    }
                }
                else if (mate != SlotIsolated && mate != SlotFixed)
                {
                    return false; // a non-terminal dead end at degree 1 can never become a valid path
                }
            }
            else if (mate != SlotIsolated && mate != SlotFixed)
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
                state[mate - 1] = SlotEndpointDone;
            }

            state[slot] = SlotIsolated; // clear so a reused slot never carries a stale, merge-blocking code
            return true;
        }
    }
}

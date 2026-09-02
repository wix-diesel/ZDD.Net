using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets that form a simple <c>s</c>–<c>t</c> path touching <b>every</b> vertex of the
    /// graph — the solution space of the traveling salesman problem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <see cref="PathSpec"/> with one rule added: a non-terminal vertex must reach degree 2 rather
    /// than being allowed to stay at degree 0. <see cref="MateChainState"/> carries the shared mechanics
    /// (both specs' <c>TakeEdge</c> reduce to the same <see cref="MateChainState.Splice"/> call, rejecting a
    /// <see cref="MateChainState.SpliceResult.Closed"/> outcome — a Hamiltonian path is still a path, not a
    /// cycle); the two differ only in which final degree
    /// <see cref="Graphs.FrontierManager.ForgottenVertices"/> vertices are allowed to leave the frontier at:
    /// <see cref="MateChainState.ForgetAllowIsolated"/> for <see cref="PathSpec"/>'s non-terminal vertices vs.
    /// <see cref="MateChainState.ForgetRequireVisited"/> here. Forcing every non-terminal vertex to degree 2
    /// is the entire mechanism that forces full vertex coverage — no separate "visited" bitset is needed.
    /// </para>
    /// <para>
    /// <b>State</b>: one <c>mate</c> code per frontier vertex, exactly as <see cref="PathSpec"/> in fixed
    /// <c>s</c>/<c>t</c> mode — no trailing counter slot is needed since there is no
    /// <see cref="PathSpec.AllowAnyEndpoints"/> equivalent here.
    /// </para>
    /// </remarks>
    public readonly struct HamiltonianPathSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int _s;
        private readonly int _t;

        /// <summary>Creates a spec for Hamiltonian <paramref name="s"/>–<paramref name="t"/> paths on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="s">One endpoint.</param>
        /// <param name="t">The other endpoint.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="s"/> or <paramref name="t"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public HamiltonianPathSpec(Graph graph, int s, int t)
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
        public Graph Graph => _graph;

        /// <summary>One endpoint.</summary>
        public int S => _s;

        /// <summary>The other endpoint.</summary>
        public int T => _t;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.EdgeCount == 0)
            {
                return DdResult.False;
            }

            // s == t: no simple path (of at least one edge) starts and ends at the same vertex.
            if (_s == _t)
            {
                return DdResult.False;
            }

            if (_graph.Degree(_s) == 0 || _graph.Degree(_t) == 0)
            {
                return DdResult.False;
            }

            // A non-terminal vertex must reach degree 2: with fewer than two incident edges in the whole
            // graph it never can, so the entire family is empty regardless of which edges are chosen.
            for (int v = 0; v < _graph.VertexCount; v++)
            {
                if (v == _s || v == _t)
                {
                    continue;
                }

                if (_graph.Degree(v) < 2)
                {
                    return DdResult.False;
                }
            }

            // state is zero-filled by the caller: every slot already reads MateChainState.SlotIsolated.
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
                int vertex = forgottenVertices[i];
                int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                bool ok = vertex == _s || vertex == _t
                    ? MateChainState.ForgetTerminal(state, slot)
                    : MateChainState.ForgetRequireVisited(state, slot);

                if (!ok)
                {
                    return DdResult.False;
                }
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }

        /// <summary>Splices the two endpoints of <paramref name="edge"/> together.</summary>
        /// <returns><see langword="false"/> if the connection is invalid (degree 3, or it would close a cycle).</returns>
        private bool TakeEdge(Span<int> state, int edgeIndex, Edge edge)
        {
            int su = _frontierManager.MateIndex(edgeIndex, edge.U);
            int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

            return MateChainState.Splice(state, su, sv) == MateChainState.SpliceResult.Spliced;
        }
    }
}

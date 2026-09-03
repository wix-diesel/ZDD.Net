using System;
using System.Collections.Generic;
using System.Linq;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets in which every terminal vertex lies in the same connected component of the
    /// resulting subgraph. The rest of the graph is free — extra components, cycles, dangling branches all
    /// count, as long as the terminals end up together. A generalization of <see cref="SpanningTreeSpec"/>'s
    /// "every vertex must be one component" down to "only these vertices must be one component"; the basis
    /// M4-5's <c>SteinerTreeSpec</c> and M4-6's <c>GraphPartitionSpec</c> build on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: a comp array like <see cref="SpanningTreeSpec"/>'s — see
    /// <see cref="ConnectedComponentState"/> for the encoding, where a code's sign marks whether its
    /// component currently contains a terminal — plus two trailing counters: how many terminals have been
    /// introduced into the frontier so far (<see cref="TerminalsSeenSlot"/>), and how many currently
    /// distinct frontier components contain a terminal (<see cref="OpenTerminalComponentCountSlot"/>).
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices as fresh singleton components (folding any
    /// terminal among them into both trailing counters), then — if the edge is taken — merge the two
    /// endpoints' components. Unlike <see cref="SpanningTreeSpec"/>, a same-component merge is never
    /// rejected: a cycle is a perfectly fine connected subgraph here. When the merge combines two
    /// previously-distinct terminal-holding components into one, <see cref="OpenTerminalComponentCountSlot"/>
    /// drops by one. Finally, for each vertex this edge forgets: if forgetting it closes a component with no
    /// terminal, that is always fine — an untouched patch of graph, free to appear or not. If it closes a
    /// component that does hold a terminal, that is only fine when every terminal has already appeared
    /// (<see cref="TerminalsSeenSlot"/> equals the terminal count) <i>and</i> this was the only currently
    /// open terminal-holding component (<see cref="OpenTerminalComponentCountSlot"/> is 1) — otherwise some
    /// terminal is stranded outside a component that can never gain another member, so the branch is
    /// rejected.
    /// </para>
    /// <para>
    /// <b>Boundary cases</b>: with zero or one terminal, no component ever needs to reach "holds every
    /// terminal" — with one terminal, whichever component holds it always already holds all of them — so
    /// every edge subset is accepted, the same family <see cref="PowerSetSpec"/> builds. With every vertex a
    /// terminal, the family becomes every connected spanning subgraph, of which the
    /// <c>VertexCount - 1</c>-edge members are exactly <see cref="SpanningTreeSpec"/>'s spanning trees. With
    /// exactly two terminals <c>s</c>, <c>t</c>, the family's <see cref="Core.Zdd.Minimal"/> members are
    /// exactly the simple <c>s</c>–<c>t</c> paths <see cref="PathSpec"/> enumerates.
    /// </para>
    /// </remarks>
    public readonly struct ConnectedSubgraphSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly bool[] _isTerminal;
        private readonly int _terminalCount;

        /// <summary>Creates a spec for edge sets that connect every one of <paramref name="terminals"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="terminals">The vertices that must end up in the same connected component.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="terminals"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A terminal is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="terminals"/> repeats a vertex.</exception>
        public ConnectedSubgraphSpec(Graph graph, IEnumerable<int> terminals)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(terminals);

            _graph = graph;
            _frontierManager = new FrontierManager(graph);

            var isTerminal = new bool[graph.VertexCount];
            int terminalCount = 0;

            foreach (int vertex in terminals)
            {
                if ((uint)vertex >= (uint)graph.VertexCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(terminals), vertex, $"Must be in 0 .. {graph.VertexCount - 1}.");
                }

                if (isTerminal[vertex])
                {
                    throw new ArgumentException($"Terminal {vertex} is repeated.", nameof(terminals));
                }

                isTerminal[vertex] = true;
                terminalCount++;
            }

            _isTerminal = isTerminal;
            _terminalCount = terminalCount;
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>The vertices that must end up in the same connected component, in ascending order.</summary>
        public IReadOnlyList<int> Terminals
        {
            get
            {
                bool[] isTerminal = _isTerminal;
                return Enumerable.Range(0, _graph.VertexCount).Where(v => isTerminal[v]).ToArray();
            }
        }

        /// <summary>
        /// The terminals-introduced counter slot: one past the last comp slot, so it can never collide with
        /// a real frontier slot (those run <c>0 .. MaxFrontierSize - 1</c>).
        /// </summary>
        private int TerminalsSeenSlot => _frontierManager.MaxFrontierSize;

        /// <summary>The open-terminal-component counter slot: one past <see cref="TerminalsSeenSlot"/>.</summary>
        private int OpenTerminalComponentCountSlot => _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize + 2;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_terminalCount >= 2)
            {
                for (int v = 0; v < _graph.VertexCount; v++)
                {
                    if (_isTerminal[v] && _graph.Degree(v) == 0)
                    {
                        return DdResult.False; // an isolated terminal can never join any other terminal
                    }
                }
            }

            if (_graph.EdgeCount == 0)
            {
                // No edges to decide: only the empty edge set exists, valid exactly when there is at most
                // one terminal to begin with (co-locating zero or one vertex is trivially free).
                return _terminalCount <= 1 ? DdResult.True : DdResult.False;
            }

            // state is zero-filled by the caller: every comp slot already reads ConnectedComponentState.SlotEmpty.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);
            int frontierLength = _frontierManager.MaxFrontierSize;

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                int vertex = introducedVertices[i];
                int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                bool isTerminal = _isTerminal[vertex];
                ConnectedComponentState.Introduce(state, slot, isTerminal);

                if (isTerminal)
                {
                    state[TerminalsSeenSlot]++;
                    state[OpenTerminalComponentCountSlot]++;
                }
            }

            if (value == 1)
            {
                int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                int sv = _frontierManager.MateIndex(edgeIndex, edge.V);
                if (ConnectedComponentState.Merge(state, frontierLength, su, sv))
                {
                    state[OpenTerminalComponentCountSlot]--;
                }
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                bool closed = ConnectedComponentState.Forget(state, frontierLength, slot, out bool hadTerminal);

                if (!closed || !hadTerminal)
                {
                    continue;
                }

                if (state[TerminalsSeenSlot] != _terminalCount || state[OpenTerminalComponentCountSlot] != 1)
                {
                    return DdResult.False; // some terminal is stranded outside a component that just sealed shut
                }

                state[OpenTerminalComponentCountSlot] = 0;
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }
}

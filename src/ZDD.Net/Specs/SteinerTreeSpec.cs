using System;
using System.Collections.Generic;
using System.Linq;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets that form a Steiner tree for a set of terminals: a connected, acyclic
    /// subgraph containing every terminal, in which every leaf is itself a terminal (the standard
    /// definition — see the remarks on why that last condition is the one that turns "a tree connecting the
    /// terminals" into "a Steiner tree"). <see cref="Core.Zdd.MinWeight{TWeight, TOps}(ReadOnlySpan{TWeight})"/>
    /// over the family gives a minimum Steiner tree; <c>TopK</c> and <c>Sample</c> draw from the same family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: <see cref="ConnectedSubgraphSpec"/>'s comp array (see <see cref="ConnectedComponentState"/>)
    /// plus its two trailing counters (terminals introduced so far, and how many currently distinct frontier
    /// components hold a terminal) — unchanged from M4-4 — plus one more array, a saturating (capped at 2)
    /// degree count per frontier vertex, mirroring <see cref="DegreeConstraintSpec"/>'s slot-per-vertex
    /// counter. The degree array is what turns "connects the terminals" into "is a tree with no wasted
    /// branches": see below.
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices exactly as <see cref="ConnectedSubgraphSpec"/>
    /// does (comp singleton, degree 0). If the edge is taken: first check whether its two endpoints already
    /// share a component via <see cref="ConnectedComponentState.SameComponent"/> — if so, taking it would
    /// close a cycle, so the branch is rejected outright (unlike <see cref="ConnectedSubgraphSpec"/>, which
    /// allows cycles; this is the same check <see cref="SpanningComponentState.TryMerge"/> makes for
    /// <see cref="SpanningTreeSpec"/>). Otherwise merge the two components (<see cref="ConnectedComponentState.Merge"/>,
    /// same terminal-counter bookkeeping as M4-4), then bump both endpoints' degree counts. Finally, for
    /// each vertex this edge forgets, its final degree in the selected edge set is now fixed (no further
    /// incident edge remains undecided): if that degree is exactly 1 — a leaf — and the vertex is not a
    /// terminal, reject; a non-terminal leaf is exactly the "wasted branch" a Steiner tree must not have,
    /// since trimming it away would only shrink the tree's weight while still connecting every terminal.
    /// Degree 0 (never touched) and degree ≥2 (an internal, possibly branching, vertex) are always fine
    /// regardless of terminal-ness. The rest — the component-closing bookkeeping that guarantees every
    /// terminal ends up in one piece — is identical to <see cref="ConnectedSubgraphSpec"/>.
    /// </para>
    /// <para>
    /// <b>Why the leaf check is enough</b>: any component that never merges with a terminal-holding one
    /// closes as its own separate non-terminal-only tree fragment. Every finite tree fragment with at least
    /// one edge has at least two leaves; since none of its vertices are terminals, the leaf check already
    /// rejects it — the first time one of those leaves is forgotten, well before the whole fragment closes.
    /// So no separate "a component with no terminal must not have used an edge" bookkeeping is needed on top
    /// of the per-vertex leaf check; the leaf check subsumes it.
    /// </para>
    /// <para>
    /// <b>Non-minimal members</b>: the family can still contain non-weight-minimal Steiner trees — e.g. a
    /// long detour between two terminals when a shorter one exists — since nothing here compares weights;
    /// only the topological shape (tree, all terminals present, every leaf a terminal) is constrained. That
    /// is the standard scope for "the family of Steiner trees": <c>MinWeight</c> picks out the cheapest
    /// member, but every topologically valid tree — minimal or not — belongs to the family itself.
    /// </para>
    /// <para>
    /// <b>Boundary cases</b>: with two terminals, a tree with exactly two leaves is necessarily a simple
    /// path (any branch point would add a third leaf, which the leaf check forbids), so the family's members
    /// are exactly <see cref="PathSpec"/>'s <c>s</c>–<c>t</c> paths. With every vertex a terminal, the leaf
    /// check is vacuous (every leaf is automatically a terminal), leaving just "acyclic and spans every
    /// vertex" — exactly <see cref="SpanningTreeSpec"/>. With zero or one terminal, any tree with at least
    /// one edge has at least two leaves, at most one of which can be the sole terminal, so the leaf check
    /// forces the only member to be the empty edge set (the trivial "tree" consisting of the terminal alone,
    /// or nothing at all).
    /// </para>
    /// </remarks>
    public readonly struct SteinerTreeSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly bool[] _isTerminal;
        private readonly int _terminalCount;

        /// <summary>Creates a spec for Steiner trees connecting <paramref name="terminals"/> on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="terminals">The vertices the tree must connect.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="terminals"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A terminal is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="terminals"/> repeats a vertex.</exception>
        public SteinerTreeSpec(Graph graph, IEnumerable<int> terminals)
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

        /// <summary>The vertices the tree must connect, in ascending order.</summary>
        public IReadOnlyList<int> Terminals
        {
            get
            {
                bool[] isTerminal = _isTerminal;
                return Enumerable.Range(0, _graph.VertexCount).Where(v => isTerminal[v]).ToArray();
            }
        }

        /// <summary>The number of comp slots — also the offset of the parallel degree array.</summary>
        private int FrontierLength => _frontierManager.MaxFrontierSize;

        /// <summary>
        /// The terminals-introduced counter slot: one past the last degree slot, so it can never collide
        /// with a real comp or degree slot.
        /// </summary>
        private int TerminalsSeenSlot => 2 * _frontierManager.MaxFrontierSize;

        /// <summary>The open-terminal-component counter slot: one past <see cref="TerminalsSeenSlot"/>.</summary>
        private int OpenTerminalComponentCountSlot => 2 * _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int ArrayLength => 2 * _frontierManager.MaxFrontierSize + 2;

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
                // one terminal to begin with (a lone terminal is trivially its own zero-edge Steiner tree).
                return _terminalCount <= 1 ? DdResult.True : DdResult.False;
            }

            // state is zero-filled by the caller: every comp slot already reads ConnectedComponentState.SlotEmpty
            // and every degree slot already reads 0.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);
            int frontierLength = FrontierLength;

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                int vertex = introducedVertices[i];
                int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                bool isTerminal = _isTerminal[vertex];
                ConnectedComponentState.Introduce(state, slot, isTerminal);
                state[frontierLength + slot] = 0;

                if (isTerminal)
                {
                    state[TerminalsSeenSlot]++;
                    state[OpenTerminalComponentCountSlot]++;
                }
            }

            int su = _frontierManager.MateIndex(edgeIndex, edge.U);
            int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

            if (value == 1)
            {
                if (ConnectedComponentState.SameComponent(state, su, sv))
                {
                    return DdResult.False; // taking this edge would close a cycle: not a tree
                }

                if (ConnectedComponentState.Merge(state, frontierLength, su, sv))
                {
                    state[OpenTerminalComponentCountSlot]--;
                }

                int duSlot = frontierLength + su;
                int dvSlot = frontierLength + sv;
                if (state[duSlot] < 2)
                {
                    state[duSlot]++;
                }

                if (state[dvSlot] < 2)
                {
                    state[dvSlot]++;
                }
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int vertex = forgottenVertices[i];
                int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                int degreeSlot = frontierLength + slot;

                if (state[degreeSlot] == 1 && !_isTerminal[vertex])
                {
                    return DdResult.False; // a non-terminal leaf: a wasted branch, not part of a Steiner tree
                }

                state[degreeSlot] = 0; // clear so a reused slot never carries a stale degree

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

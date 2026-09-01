using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets that form a spanning forest of a graph: an acyclic subgraph (every vertex
    /// belongs to exactly one tree, including single-vertex trees for edgeless vertices), optionally
    /// constrained to an exact number of trees via <see cref="Components"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shares its comp-array mechanics with <see cref="SpanningTreeSpec"/> — see
    /// <see cref="SpanningComponentState"/> for the encoding and canonicalization. The two specs differ only
    /// in what happens when a component closes: <see cref="SpanningTreeSpec"/> demands exactly one, ever;
    /// this spec allows any number of components to close at any time (a forest may have many trees), only
    /// checking the final total against <see cref="Components"/> when that count is given.
    /// </para>
    /// <para>
    /// <b>Isolated vertices</b>: a vertex with no incident edges at all never enters the frontier (it is
    /// introduced and forgotten by no edge), so it is invisible to the per-edge comp-array processing. It
    /// is still a genuine single-vertex tree of the forest, so its count is folded in separately as
    /// <c>isolatedVertexCount</c>, computed once in the constructor.
    /// </para>
    /// </remarks>
    public readonly struct ForestSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int? _components;
        private readonly int _isolatedVertexCount;

        /// <summary>Creates a spec for spanning forests of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="components">
        /// When given, only forests with exactly this many trees are accepted. When <see langword="null"/>,
        /// any number of trees is accepted (any acyclic edge subset).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="components"/> is not positive.</exception>
        public ForestSpec(Graph graph, int? components = null)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (components is int c && c <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(components), c, "Must be positive.");
            }

            _graph = graph;
            _components = components;
            _frontierManager = new FrontierManager(graph);

            int isolated = 0;
            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (graph.Degree(v) == 0)
                {
                    isolated++;
                }
            }

            _isolatedVertexCount = isolated;
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>The required number of trees, or <see langword="null"/> if any number is accepted.</summary>
        public int? Components => _components;

        /// <summary>
        /// The closed-component-counter slot: one past the last comp slot, so it can never collide with a
        /// real frontier slot (those run <c>0 .. MaxFrontierSize - 1</c>).
        /// </summary>
        private int ClosedComponentCountSlot => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_components is int target && _isolatedVertexCount > target)
            {
                return DdResult.False; // the edgeless vertices alone already outnumber the target
            }

            if (_graph.EdgeCount == 0)
            {
                if (_components is int t)
                {
                    return _graph.VertexCount == t ? DdResult.True : DdResult.False;
                }

                return DdResult.True; // no edges at all: every vertex is its own tree, always acyclic
            }

            // state is zero-filled by the caller: every comp slot already reads SpanningComponentState.SlotEmpty.
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
                SpanningComponentState.Introduce(state, _frontierManager.MateIndex(edgeIndex, introducedVertices[i]));
            }

            if (value == 1)
            {
                int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                int sv = _frontierManager.MateIndex(edgeIndex, edge.V);
                if (!SpanningComponentState.TryMerge(state, frontierLength, su, sv))
                {
                    return DdResult.False;
                }
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                bool closed = SpanningComponentState.Forget(state, frontierLength, slot);

                if (closed && _components is int target)
                {
                    int closedCount = state[ClosedComponentCountSlot] + 1;
                    if (closedCount + _isolatedVertexCount > target)
                    {
                        return DdResult.False; // already too many trees to ever hit the target
                    }

                    state[ClosedComponentCountSlot] = closedCount;
                }
            }

            int remaining = level - 1;
            if (remaining > 0)
            {
                return remaining;
            }

            if (_components is int expected)
            {
                return state[ClosedComponentCountSlot] + _isolatedVertexCount == expected ? DdResult.True : DdResult.False;
            }

            return DdResult.True;
        }
    }
}

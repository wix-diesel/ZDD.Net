using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets in which every vertex group ends up as its own connected component: two
    /// vertices from the <i>same</i> group always share a component, and two vertices from <i>different</i>
    /// groups never do &#8212; Graphillion's <c>graphs(vertex_groups=...)</c>. A generalization of
    /// <see cref="ConnectedSubgraphSpec"/>'s single terminal set to several mutually-exclusive ones at once:
    /// useful for routing several terminal pairs simultaneously, or for districting constraints where each
    /// district must stay whole and separate from the others.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Free vertices</b>: a vertex that belongs to no group is unconstrained &#8212; it may sit alone, join
    /// any single group's component, or bridge several free vertices together, but it may never end up in a
    /// component that also contains two different groups (that would violate both groups at once). This
    /// matches Graphillion's own <c>vertex_groups</c> behavior: ungrouped vertices are free to use, but may
    /// not connect two groups.
    /// </para>
    /// <para>
    /// <b>State</b>: a comp array like <see cref="GraphPartitionSpec"/>'s &#8212; see
    /// <see cref="VertexGroupComponentState"/> for the encoding, where each component's parallel entry records
    /// which group (if any) its members have committed to &#8212; plus, for every group, two trailing
    /// counters: how many of its members have been introduced into the frontier so far, and how many
    /// currently distinct frontier components are bound to it.
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices as fresh singleton components (each bound to its
    /// own vertex's group, incrementing that group's two trailing counters), then &#8212; if the edge is taken
    /// &#8212; merge the two endpoints' components. Unlike <see cref="SpanningTreeSpec"/>, a same-component
    /// merge is never rejected: a cycle is a perfectly fine member of this family. A merge between two
    /// components already bound to two <i>different</i> groups is rejected outright (⊥); merging a
    /// group-bound component with a free-only one simply carries the group forward, and merging two
    /// components already bound to the <i>same</i> group drops that group's open-component count by one.
    /// Finally, for each vertex this edge forgets: if forgetting it closes a component with no bound group,
    /// that is always fine &#8212; an untouched patch of graph, free to appear or not. If it closes a
    /// component bound to some group, that is only fine when every one of that group's members has already
    /// appeared <i>and</i> this was the only currently open component bound to it &#8212; otherwise some
    /// member of the group is stranded outside a component that can never gain another member, so the
    /// branch is rejected.
    /// </para>
    /// <para>
    /// <b>Boundary cases</b>: an empty group is vacuously satisfied and never affects any branch. A
    /// single-vertex group is likewise always satisfied &#8212; whichever component ends up holding it already
    /// holds its one member &#8212; exactly like <see cref="ConnectedSubgraphSpec"/> with one terminal. With
    /// zero groups, or every group empty, every edge subset is accepted, the same family <see cref="PowerSetSpec"/>
    /// builds. With exactly one non-empty group, this is exactly <see cref="ConnectedSubgraphSpec"/> over
    /// that group's members: the "different groups must stay apart" half of the constraint is vacuous with
    /// only one group, leaving only "this group's members must share a component."
    /// </para>
    /// </remarks>
    public readonly struct VertexGroupSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int[] _vertexGroup;
        private readonly int[] _groupSize;

        /// <summary>Creates a spec for edge sets that turn every one of <paramref name="groups"/> into its own component.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="groups">
        /// The vertex groups: vertices within one group must end up in the same connected component,
        /// vertices from different groups must not. A vertex absent from every group is free (see remarks).
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="graph"/> or <paramref name="groups"/> is <see langword="null"/>, or one of
        /// <paramref name="groups"/>'s entries is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">A vertex is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        /// <exception cref="ArgumentException">A vertex is repeated, whether within one group or across two.</exception>
        public VertexGroupSpec(Graph graph, IReadOnlyList<IReadOnlyList<int>> groups)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(groups);

            _graph = graph;
            _frontierManager = new FrontierManager(graph);

            var vertexGroup = new int[graph.VertexCount];
            var groupSize = new int[groups.Count];

            for (int g = 0; g < groups.Count; g++)
            {
                IReadOnlyList<int> group = groups[g];
                ArgumentNullException.ThrowIfNull(group, nameof(groups));

                int id = g + 1;
                int size = 0;

                foreach (int vertex in group)
                {
                    if ((uint)vertex >= (uint)graph.VertexCount)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(groups), vertex, $"Must be in 0 .. {graph.VertexCount - 1}.");
                    }

                    if (vertexGroup[vertex] != 0)
                    {
                        throw new ArgumentException($"Vertex {vertex} is repeated, within a group or across two.", nameof(groups));
                    }

                    vertexGroup[vertex] = id;
                    size++;
                }

                groupSize[g] = size;
            }

            _vertexGroup = vertexGroup;
            _groupSize = groupSize;
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>The number of vertex groups (including any that turned out empty).</summary>
        public int GroupCount => _groupSize.Length;

        /// <summary>The vertex groups, each in ascending vertex order, in the order they were supplied.</summary>
        public IReadOnlyList<IReadOnlyList<int>> Groups
        {
            get
            {
                var lists = new List<int>[_groupSize.Length];
                for (int g = 0; g < lists.Length; g++)
                {
                    lists[g] = new List<int>();
                }

                for (int v = 0; v < _graph.VertexCount; v++)
                {
                    int g = _vertexGroup[v];
                    if (g != 0)
                    {
                        lists[g - 1].Add(v);
                    }
                }

                var result = new IReadOnlyList<int>[lists.Length];
                for (int g = 0; g < lists.Length; g++)
                {
                    result[g] = lists[g].ToArray();
                }

                return result;
            }
        }

        /// <summary>The number of comp slots — also the offset of the parallel group-of-component array.</summary>
        private int FrontierLength => _frontierManager.MaxFrontierSize;

        /// <summary>
        /// The offset of the per-group introduced-count counters: one past the group-of-component array, so
        /// it can never collide with a real comp or group slot.
        /// </summary>
        private int SeenSlotOffset => 2 * _frontierManager.MaxFrontierSize;

        /// <summary>The offset of the per-group open-component counters: one past the seen counters.</summary>
        private int OpenCountSlotOffset => SeenSlotOffset + _groupSize.Length;

        /// <inheritdoc/>
        public int ArrayLength => SeenSlotOffset + 2 * _groupSize.Length;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            for (int v = 0; v < _graph.VertexCount; v++)
            {
                int g = _vertexGroup[v];
                if (g != 0 && _groupSize[g - 1] >= 2 && _graph.Degree(v) == 0)
                {
                    return DdResult.False; // an isolated vertex can never join the rest of its group
                }
            }

            if (_graph.EdgeCount == 0)
            {
                // No edges to decide: only the empty edge set exists, valid exactly when every group already
                // has at most one member (co-locating zero or one vertex is trivially free).
                for (int g = 0; g < _groupSize.Length; g++)
                {
                    if (_groupSize[g] >= 2)
                    {
                        return DdResult.False;
                    }
                }

                return DdResult.True;
            }

            // state is zero-filled by the caller: every comp slot already reads VertexGroupComponentState.SlotEmpty.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);
            int frontierLength = FrontierLength;
            int seenOffset = SeenSlotOffset;
            int openOffset = OpenCountSlotOffset;

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                int vertex = introducedVertices[i];
                int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                int group = _vertexGroup[vertex];
                VertexGroupComponentState.Introduce(state, frontierLength, slot, group);

                if (group != 0)
                {
                    state[seenOffset + group - 1]++;
                    state[openOffset + group - 1]++;
                }
            }

            if (value == 1)
            {
                int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

                if (!VertexGroupComponentState.Merge(state, frontierLength, su, sv, out bool joinedSameGroup))
                {
                    return DdResult.False; // this edge would join two different vertex groups into one component
                }

                if (joinedSameGroup)
                {
                    int mergedRep = state[su] - 1;
                    int group = state[frontierLength + mergedRep];
                    state[openOffset + group - 1]--;
                }
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                bool closed = VertexGroupComponentState.Forget(state, frontierLength, slot, out int closedGroup);

                if (!closed || closedGroup == 0)
                {
                    continue;
                }

                int g = closedGroup - 1;
                if (state[seenOffset + g] != _groupSize[g] || state[openOffset + g] != 1)
                {
                    return DdResult.False; // some member of this group is stranded outside a component that just sealed shut
                }

                state[openOffset + g] = 0;
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }
}

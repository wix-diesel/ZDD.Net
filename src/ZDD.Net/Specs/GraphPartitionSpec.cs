using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets whose "kept" edges (an edge is in the set means its two endpoints stay in
    /// the same block; excluded means the edge is cut) split the graph into exactly <c>K</c> connected
    /// blocks, each with a vertex count in <c>[MinBlockSize, MaxBlockSize]</c> — the balance constraint. The
    /// electoral-districting / regional-partition use case ROADMAP.md's M4-6 names: enumerate every k-way
    /// split satisfying a size balance, then pick among them by whatever score matters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: <see cref="PartitionComponentState"/>'s comp array (one code per frontier vertex) plus
    /// its parallel per-component vertex-count array, plus one trailing counter for how many components
    /// have closed so far among vertices actually seen on some edge. A degree-0 vertex never appears in any
    /// edge, so it never enters the comp array at all; each contributes a fixed, unavoidable size-1 block of
    /// its own, accounted for separately via <see cref="IsolatedVertexCount"/> rather than through the
    /// per-edge machinery below.
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices as fresh singleton, size-1 components. If the
    /// edge is taken (kept), merge the two endpoints' components, summing their sizes — unlike
    /// <see cref="SpanningTreeSpec"/>, a same-component merge is never rejected, since a cycle within one
    /// block is perfectly fine. Finally, for each vertex this edge forgets: if forgetting it closes a
    /// component (no other frontier vertex belongs to it anymore), that component's final size is now fixed
    /// — reject if it falls outside <c>[MinBlockSize, MaxBlockSize]</c>, otherwise count it as one more
    /// closed block, rejecting outright once the closed count (plus the isolated vertices, which are always
    /// "closed") would exceed <see cref="K"/>. At the very last edge, every vertex has necessarily been
    /// forgotten, so the accept/reject decision there also confirms the closed count reached exactly
    /// <see cref="K"/> — not merely never exceeded it.
    /// </para>
    /// <para>
    /// <b><c>K == 1</c></b>: with <c>MinBlockSize &lt;= 1</c> and <c>MaxBlockSize &gt;= VertexCount</c> (a
    /// non-binding balance range), this collapses to exactly the family
    /// <see cref="ConnectedSubgraphSpec"/> builds when every vertex is a terminal: both require the whole
    /// graph to end up as a single connected piece, and otherwise place no constraint on which edges are
    /// taken.
    /// </para>
    /// </remarks>
    public readonly struct GraphPartitionSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int _k;
        private readonly int _minBlockSize;
        private readonly int _maxBlockSize;
        private readonly int _isolatedVertexCount;

        /// <summary>Creates a spec for <paramref name="k"/>-way balanced partitions of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to partition.</param>
        /// <param name="k">The required number of blocks (connected components of the kept edges).</param>
        /// <param name="minBlockSize">The minimum number of vertices a block may have.</param>
        /// <param name="maxBlockSize">The maximum number of vertices a block may have.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="k"/> or <paramref name="minBlockSize"/> is not positive, or <paramref name="maxBlockSize"/>
        /// is less than <paramref name="minBlockSize"/>.
        /// </exception>
        public GraphPartitionSpec(Graph graph, int k, int minBlockSize, int maxBlockSize)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (k <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(k), k, "Must be positive.");
            }

            if (minBlockSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minBlockSize), minBlockSize, "Must be positive.");
            }

            if (maxBlockSize < minBlockSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxBlockSize), maxBlockSize, $"Must not be less than minBlockSize ({minBlockSize}).");
            }

            _graph = graph;
            _k = k;
            _minBlockSize = minBlockSize;
            _maxBlockSize = maxBlockSize;
            _frontierManager = new FrontierManager(graph);

            int isolatedVertexCount = 0;
            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (graph.Degree(v) == 0)
                {
                    isolatedVertexCount++;
                }
            }

            _isolatedVertexCount = isolatedVertexCount;
        }

        /// <summary>The graph this spec partitions.</summary>
        public Graph Graph => _graph;

        /// <summary>The required number of blocks.</summary>
        public int K => _k;

        /// <summary>The minimum number of vertices a block may have.</summary>
        public int MinBlockSize => _minBlockSize;

        /// <summary>The maximum number of vertices a block may have.</summary>
        public int MaxBlockSize => _maxBlockSize;

        /// <summary>The number of degree-0 vertices, each an unavoidable size-1 block of its own.</summary>
        public int IsolatedVertexCount => _isolatedVertexCount;

        /// <summary>The number of comp slots — also the offset of the parallel size array.</summary>
        private int FrontierLength => _frontierManager.MaxFrontierSize;

        /// <summary>
        /// The closed-block-counter slot: one past the last size slot, so it can never collide with a real
        /// comp or size slot.
        /// </summary>
        private int ClosedCountSlot => 2 * _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => 2 * _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_isolatedVertexCount > 0 && _minBlockSize > 1)
            {
                return DdResult.False; // an isolated vertex is its own size-1 block; the balance range must allow size 1
            }

            if (_graph.EdgeCount == 0)
            {
                // No edges to decide: every vertex is its own block, valid iff that count is exactly k
                // (size 1 is already known to satisfy the balance range, from the check above).
                return _graph.VertexCount == _k ? DdResult.True : DdResult.False;
            }

            if (_isolatedVertexCount > _k)
            {
                return DdResult.False; // the isolated vertices alone already exceed k blocks
            }

            // state is zero-filled by the caller: every comp slot already reads PartitionComponentState.SlotEmpty.
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
                int slot = _frontierManager.MateIndex(edgeIndex, introducedVertices[i]);
                PartitionComponentState.Introduce(state, frontierLength, slot);
            }

            if (value == 1)
            {
                int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                int sv = _frontierManager.MateIndex(edgeIndex, edge.V);
                PartitionComponentState.Merge(state, frontierLength, su, sv);
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                bool closed = PartitionComponentState.Forget(state, frontierLength, slot, out int closedSize);

                if (!closed)
                {
                    continue;
                }

                if (closedSize < _minBlockSize || closedSize > _maxBlockSize)
                {
                    return DdResult.False; // this block's final size falls outside the balance range
                }

                int closedCount = state[ClosedCountSlot] + 1;
                if (closedCount + _isolatedVertexCount > _k)
                {
                    return DdResult.False; // one more block than k allows, and blocks only ever accumulate
                }

                state[ClosedCountSlot] = closedCount;
            }

            int remaining = level - 1;
            if (remaining > 0)
            {
                return remaining;
            }

            // Every vertex has been forgotten by now: the closed count must have reached exactly k, not just
            // stayed under it.
            return state[ClosedCountSlot] + _isolatedVertexCount == _k ? DdResult.True : DdResult.False;
        }
    }
}

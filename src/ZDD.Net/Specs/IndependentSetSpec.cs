using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of independent sets of a graph: vertex sets in which no two vertices are adjacent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Variables are vertices, not edges</b> — see <see cref="VertexFrontierManager"/>. Vertex <c>v</c>
    /// is variable index <c>v</c>, decided in ascending order; branch <c>1</c> means "<c>v</c> is in the
    /// set".
    /// </para>
    /// <para>
    /// <b>State</b>: one included/excluded bit per frontier vertex, held in the state slot
    /// <see cref="VertexFrontierManager.Slot"/> assigns it.
    /// </para>
    /// <para>
    /// <b>Per vertex</b> <c>v</c>: if <c>v</c> is taken, reject it if any already-decided lower-indexed
    /// neighbor is already in the set (that pair would be adjacent), otherwise accept. Once <c>v</c>'s
    /// highest-indexed neighbor has been decided, its slot is no longer needed by any future vertex; it is
    /// reset so a slot later reused by another vertex never inherits a stale flag.
    /// </para>
    /// </remarks>
    public readonly struct IndependentSetSpec : IArrayDdSpec
    {
        private const int Excluded = 0;
        private const int Included = 1;

        private readonly Graph _graph;
        private readonly VertexFrontierManager _frontierManager;

        /// <summary>Creates a spec for independent sets of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public IndependentSetSpec(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _graph = graph;
            _frontierManager = new VertexFrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state) =>
            // state is zero-filled by the caller: every slot already reads Excluded.
            _graph.VertexCount;

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int vertex = _frontierManager.LevelToVertex(level);
            int slot = _frontierManager.Slot(vertex);

            if (value == Included)
            {
                IReadOnlyList<int> earlierNeighborSlots = _frontierManager.EarlierNeighborSlots(vertex);
                for (int i = 0; i < earlierNeighborSlots.Count; i++)
                {
                    if (state[earlierNeighborSlots[i]] == Included)
                    {
                        return DdResult.False; // an already-selected neighbor: this pair would be adjacent
                    }
                }
            }

            state[slot] = value;

            IReadOnlyList<int> forgottenSlots = _frontierManager.ForgottenSlots(vertex);
            for (int i = 0; i < forgottenSlots.Count; i++)
            {
                state[forgottenSlots[i]] = Excluded; // clear so a reused slot never carries a stale flag
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }
}

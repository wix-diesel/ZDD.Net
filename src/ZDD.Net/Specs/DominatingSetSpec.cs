using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of dominating sets of a graph: vertex sets in which every vertex is either in the set
    /// itself or adjacent to a vertex that is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Variables are vertices, not edges</b> — see <see cref="VertexFrontierManager"/>. Vertex <c>v</c>
    /// is variable index <c>v</c>, decided in ascending order; branch <c>1</c> means "<c>v</c> is in the
    /// set".
    /// </para>
    /// <para>
    /// <b>State</b>: one three-value code per frontier vertex, held in the state slot
    /// <see cref="VertexFrontierManager.Slot"/> assigns it — <see cref="Selected"/> (in the set),
    /// <see cref="DominatedUnselected"/> (not in the set, but an already-decided neighbor is), or
    /// <see cref="NotDominated"/> (neither, yet).
    /// </para>
    /// <para>
    /// <b>Per vertex</b> <c>v</c>: if <c>v</c> is taken, mark it <see cref="Selected"/> and promote any
    /// already-decided lower-indexed neighbor still <see cref="NotDominated"/> to
    /// <see cref="DominatedUnselected"/> (a later neighbor of <c>v</c> picks this up automatically, by
    /// finding <c>v</c> at <see cref="Selected"/> when it is itself decided). If <c>v</c> is left out, its
    /// own code becomes <see cref="DominatedUnselected"/> when some already-decided lower-indexed neighbor
    /// is <see cref="Selected"/>, or <see cref="NotDominated"/> otherwise. Once <c>v</c>'s highest-indexed
    /// neighbor has been decided (or immediately, for a vertex with no higher-indexed neighbor), its slot
    /// is checked one last time — <see cref="NotDominated"/> there means it can never be dominated by
    /// anything still to come, so the branch is rejected — and then reset so a slot later reused by another
    /// vertex never inherits a stale code.
    /// </para>
    /// </remarks>
    public readonly struct DominatingSetSpec : IArrayDdSpec
    {
        private const int NotDominated = 0;
        private const int DominatedUnselected = 1;
        private const int Selected = 2;

        private readonly Graph _graph;
        private readonly VertexFrontierManager _frontierManager;

        /// <summary>Creates a spec for dominating sets of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public DominatingSetSpec(Graph graph)
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
            // state is zero-filled by the caller: every slot already reads NotDominated.
            _graph.VertexCount;

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int vertex = _frontierManager.LevelToVertex(level);
            int slot = _frontierManager.Slot(vertex);
            IReadOnlyList<int> earlierNeighborSlots = _frontierManager.EarlierNeighborSlots(vertex);

            if (value == 1)
            {
                state[slot] = Selected;

                for (int i = 0; i < earlierNeighborSlots.Count; i++)
                {
                    int neighborSlot = earlierNeighborSlots[i];
                    if (state[neighborSlot] == NotDominated)
                    {
                        state[neighborSlot] = DominatedUnselected;
                    }
                }
            }
            else
            {
                bool dominated = false;
                for (int i = 0; i < earlierNeighborSlots.Count; i++)
                {
                    if (state[earlierNeighborSlots[i]] == Selected)
                    {
                        dominated = true;
                        break;
                    }
                }

                state[slot] = dominated ? DominatedUnselected : NotDominated;
            }

            IReadOnlyList<int> forgottenSlots = _frontierManager.ForgottenSlots(vertex);
            for (int i = 0; i < forgottenSlots.Count; i++)
            {
                int forgottenSlot = forgottenSlots[i];
                if (state[forgottenSlot] == NotDominated)
                {
                    return DdResult.False; // no more incident vertices can ever dominate this one
                }

                state[forgottenSlot] = NotDominated; // clear so a reused slot never carries a stale code
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }
}

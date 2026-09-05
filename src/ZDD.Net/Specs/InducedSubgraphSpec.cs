using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets induced by some vertex subset <c>S</c>: for every edge <c>(u,v)</c> of the
    /// graph, <c>(u,v)</c> is in the set exactly when both <c>u</c> and <c>v</c> are in <c>S</c>. Unlike an
    /// ordinary subgraph, an edge with both endpoints selected cannot be left out — Graphillion's
    /// <c>induced_graphs</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>S</c> is not a parameter</b>: the family ranges over every <c>S &#8838; V</c>, not one fixed
    /// set. Given a member edge set <c>F</c>, the only <c>S</c> that could have produced it is
    /// <c>S = </c> the vertices <c>F</c> touches — any edge with both endpoints there is forced into
    /// <c>F</c>, and adding an untouched vertex to <c>S</c> would force in whichever of its edges land
    /// inside <c>S</c>, changing <c>F</c>. So an isolated vertex (no edge of <c>F</c> touches it) is
    /// effectively <c>Out</c> regardless of which side of <c>S</c> it is actually on, and the family is
    /// uniquely determined as a set of edge sets. Connectivity is not required (matching Graphillion); for
    /// connected induced subgraphs, build this family and <see cref="ConnectedSubgraphSpec"/>'s separately
    /// and intersect the two <see cref="Core.Zdd"/> results (<see cref="Core.Zdd.Intersect(Core.Zdd)"/>).
    /// </para>
    /// <para>
    /// <b>State</b>: one <see cref="InducedVertexState"/> value per frontier vertex —
    /// <see cref="InducedVertexState.Unknown"/>, <see cref="InducedVertexState.In"/> (touched by a taken
    /// edge, i.e. confirmed in <c>S</c>), or <see cref="InducedVertexState.Out"/> (confirmed never touched,
    /// i.e. confirmed out of — or irrelevant to — <c>S</c>). No side counters: three values fit in the same
    /// slot bit-packing (M3-2) already gives every other array-based spec.
    /// </para>
    /// <para>
    /// <b>Per edge</b> <c>(u,v)</c>: taking it requires both endpoints eventually <c>In</c> — reject
    /// outright if either is already <see cref="InducedVertexState.Out"/>, then move each still-
    /// <see cref="InducedVertexState.Unknown"/> endpoint to <see cref="InducedVertexState.In"/> via
    /// <see cref="InducedVertexState.MarkIn"/> (see its remarks, and the type remarks below, for why this
    /// is exactly where a not-taken edge to an already-<c>In</c> vertex is finally caught). Not taking it
    /// forbids both ending up <c>In</c>: reject outright if both already are, otherwise
    /// <see cref="InducedVertexState.MarkNotAdjacent"/> fixes whichever side is still
    /// <see cref="InducedVertexState.Unknown"/> to <see cref="InducedVertexState.Out"/> when the other is
    /// already <c>In</c> (see its remarks for why this can't wait). When both sides are still
    /// <c>Unknown</c>, neither is touched — resolved later, if at all, by <see cref="InducedVertexState.MarkIn"/>.
    /// </para>
    /// <para>
    /// <b>Forgetting a vertex</b>: <c>In</c> or <c>Out</c>, it plays no further part and its slot is simply
    /// reset to <see cref="InducedVertexState.Unknown"/> for reuse — the reset value doubles as "confirmed
    /// out" for a vertex that stayed <c>Unknown</c> its entire time in the frontier (it can never take
    /// another edge now, so it is exactly the "isolated in the result" case described above).
    /// </para>
    /// </remarks>
    public readonly struct InducedSubgraphSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;

        // Per edge index, per endpoint: the frontier slots of graph-neighbors of that endpoint connected by
        // an earlier edge and still present in the frontier at this edge — see InducedVertexState.MarkIn.
        // Precomputed once from graph structure alone, since it never depends on which edges get taken.
        private readonly int[][] _priorNeighborSlotsU;
        private readonly int[][] _priorNeighborSlotsV;

        /// <summary>Creates a spec for the edge sets that some vertex subset of <paramref name="graph"/> induces.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public InducedSubgraphSpec(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _graph = graph;
            _frontierManager = new FrontierManager(graph);

            int edgeCount = graph.EdgeCount;
            var priorSlotsU = new int[edgeCount][];
            var priorSlotsV = new int[edgeCount][];

            for (int v = 0; v < graph.VertexCount; v++)
            {
                IReadOnlyList<int> incident = graph.IncidentEdges(v);
                if (incident.Count == 0)
                {
                    continue;
                }

                // Neighbors of v seen so far, still present in the frontier as of the edge about to be
                // processed (pruned below); each edge of v snapshots this list before adding its own
                // neighbor, so a neighbor never sees itself and only ever appears in *later* snapshots.
                var active = new List<(int Neighbor, int NeighborLastEdge)>();

                for (int k = 0; k < incident.Count; k++)
                {
                    int edgeIndex = incident[k];
                    active.RemoveAll(entry => entry.NeighborLastEdge < edgeIndex);

                    var slots = new int[active.Count];
                    for (int i = 0; i < active.Count; i++)
                    {
                        slots[i] = _frontierManager.MateIndex(edgeIndex, active[i].Neighbor);
                    }

                    Edge edge = graph.GetEdge(edgeIndex);
                    if (edge.U == v)
                    {
                        priorSlotsU[edgeIndex] = slots;
                    }
                    else
                    {
                        priorSlotsV[edgeIndex] = slots;
                    }

                    int neighbor = edge.U == v ? edge.V : edge.U;
                    IReadOnlyList<int> neighborIncident = graph.IncidentEdges(neighbor);
                    active.Add((neighbor, neighborIncident[neighborIncident.Count - 1]));
                }
            }

            _priorNeighborSlotsU = priorSlotsU;
            _priorNeighborSlotsV = priorSlotsV;
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state) =>
            // state is zero-filled by the caller: every slot already reads InducedVertexState.Unknown.
            _graph.EdgeCount > 0 ? _graph.EdgeCount : DdResult.True;

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);
            int su = _frontierManager.MateIndex(edgeIndex, edge.U);
            int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

            if (value == 1)
            {
                if (state[su] == InducedVertexState.Out || state[sv] == InducedVertexState.Out)
                {
                    return DdResult.False;
                }

                if (state[su] == InducedVertexState.Unknown &&
                    !InducedVertexState.MarkIn(state, su, _priorNeighborSlotsU[edgeIndex]))
                {
                    return DdResult.False;
                }

                if (state[sv] == InducedVertexState.Unknown &&
                    !InducedVertexState.MarkIn(state, sv, _priorNeighborSlotsV[edgeIndex]))
                {
                    return DdResult.False;
                }
            }
            else
            {
                if (state[su] == InducedVertexState.In && state[sv] == InducedVertexState.In)
                {
                    return DdResult.False;
                }

                InducedVertexState.MarkNotAdjacent(state, su, sv);
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                state[_frontierManager.MateIndex(edgeIndex, forgottenVertices[i])] = InducedVertexState.Unknown;
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }
}

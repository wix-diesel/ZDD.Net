using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// Precomputes the frontier-method bookkeeping for spec families whose variables are vertices
    /// (docs/PLAN.md §7.2's "頂点の族" — <see cref="Specs.IndependentSetSpec"/> and friends), not edges.
    /// <see cref="FrontierManager"/>'s counterpart: same slot-reuse idea, keyed by vertex instead of edge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Variable order</b>: vertices are decided in ascending index order — vertex <c>v</c> <i>is</i>
    /// variable index <c>v</c>, the vertex analogue of <see cref="Graph.EdgeIndexToVariableIndex"/> being
    /// an identity for edge-indexed specs. There is no vertex-reordering equivalent of
    /// <see cref="Graph.Optimize"/> yet (docs/PLAN.md §7.2 notes picking a better vertex numbering, the
    /// way M3-1 picks a better edge order, as future work); this type's shape does not preclude adding one
    /// later.
    /// </para>
    /// <para>
    /// A vertex <c>v</c> enters the frontier the moment it is decided — it needs a slot immediately, to be
    /// checked against (and to update) any already-decided lower-indexed neighbor — and leaves right after
    /// its highest-indexed neighbor is decided, or right after its own decision if every neighbor has a
    /// lower index (or it has none): once that point passes, no later vertex ever needs to know its state
    /// again.
    /// </para>
    /// <para>
    /// Everything is precomputed once in the constructor in <c>O(VertexCount + EdgeCount)</c> and read
    /// from arrays afterwards, so it stays cheap to consult from inside a spec's <c>GetChild</c> hot path.
    /// </para>
    /// </remarks>
    public sealed class VertexFrontierManager
    {
        private readonly int[] _slotOfVertex;
        private readonly int[][] _earlierNeighborSlots;
        private readonly ReadOnlyCollection<int>[] _earlierNeighborSlotsView;
        private readonly int[][] _forgottenSlots;
        private readonly ReadOnlyCollection<int>[] _forgottenSlotsView;

        /// <summary>Precomputes the frontier bookkeeping for <paramref name="graph"/>'s vertices, in ascending index order.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public VertexFrontierManager(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            Graph = graph;
            int vertexCount = graph.VertexCount;

            // lastRelevant[v]: the last vertex whose decision still needs v's slot — v's own index, or its
            // highest-indexed neighbor's index if that is larger.
            var lastRelevant = new int[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                lastRelevant[v] = v;
                IReadOnlyList<int> incident = graph.IncidentEdges(v);
                for (int i = 0; i < incident.Count; i++)
                {
                    int u = graph.GetEdge(incident[i]).Other(v);
                    if (u > lastRelevant[v])
                    {
                        lastRelevant[v] = u;
                    }
                }
            }

            // Bucket vertices to forget by the vertex whose decision retires them, and count each vertex's
            // lower-indexed neighbors, in a single counting pass (no per-vertex lists to grow).
            var forgottenCounts = new int[vertexCount];
            var earlierCounts = new int[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                forgottenCounts[lastRelevant[v]]++;

                IReadOnlyList<int> incident = graph.IncidentEdges(v);
                for (int i = 0; i < incident.Count; i++)
                {
                    if (graph.GetEdge(incident[i]).Other(v) < v)
                    {
                        earlierCounts[v]++;
                    }
                }
            }

            var forgottenVertices = new int[vertexCount][];
            _forgottenSlots = new int[vertexCount][];
            _forgottenSlotsView = new ReadOnlyCollection<int>[vertexCount];
            _earlierNeighborSlots = new int[vertexCount][];
            _earlierNeighborSlotsView = new ReadOnlyCollection<int>[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                forgottenVertices[v] = new int[forgottenCounts[v]];
                _forgottenSlots[v] = new int[forgottenCounts[v]];
                _forgottenSlotsView[v] = new ReadOnlyCollection<int>(_forgottenSlots[v]);
                _earlierNeighborSlots[v] = new int[earlierCounts[v]];
                _earlierNeighborSlotsView[v] = new ReadOnlyCollection<int>(_earlierNeighborSlots[v]);
            }

            var forgottenFill = new int[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                int at = lastRelevant[v];
                forgottenVertices[at][forgottenFill[at]++] = v;
            }

            // Slot assignment + earlier-neighbor lookup, one forward pass over vertices in index order, LIFO
            // free pool: reuse any slot freed by an already-forgotten vertex, or allocate a fresh one. A new
            // slot is only allocated when the pool is empty, so the total slot count equals the frontier's
            // peak size (see FrontierManager for the same argument over edges).
            _slotOfVertex = new int[vertexCount];
            var freeSlots = new Stack<int>();
            int nextSlot = 0;
            int frontierCount = 0;
            int maxFrontierSize = 0;
            var earlierFill = new int[vertexCount];

            for (int v = 0; v < vertexCount; v++)
            {
                int slot = freeSlots.Count > 0 ? freeSlots.Pop() : nextSlot++;
                _slotOfVertex[v] = slot;
                frontierCount++;
                if (frontierCount > maxFrontierSize)
                {
                    maxFrontierSize = frontierCount;
                }

                IReadOnlyList<int> incident = graph.IncidentEdges(v);
                for (int i = 0; i < incident.Count; i++)
                {
                    int u = graph.GetEdge(incident[i]).Other(v);
                    if (u < v)
                    {
                        _earlierNeighborSlots[v][earlierFill[v]++] = _slotOfVertex[u];
                    }
                }

                int fi = 0;
                foreach (int forgotten in forgottenVertices[v])
                {
                    int freedSlot = _slotOfVertex[forgotten];
                    _forgottenSlots[v][fi++] = freedSlot;
                    freeSlots.Push(freedSlot);
                    frontierCount--;
                }
            }

            MaxFrontierSize = maxFrontierSize;
        }

        /// <summary>The graph this instance was built from.</summary>
        public Graph Graph { get; }

        /// <summary>
        /// The largest frontier size over all vertices — the state size a vertex-indexed frontier-method
        /// spec must carry, and what its build's time and memory sit on the exponent of.
        /// </summary>
        public int MaxFrontierSize { get; }

        /// <summary>
        /// The state-array slot <paramref name="vertex"/> occupies from the moment it is decided until it
        /// is forgotten. Unique among vertices simultaneously in the frontier.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public int Slot(int vertex)
        {
            ValidateVertex(vertex);
            return _slotOfVertex[vertex];
        }

        /// <summary>
        /// The frontier slots of <paramref name="vertex"/>'s neighbors that were decided before it (lower
        /// vertex index). Order follows <see cref="Graph.IncidentEdges"/> (edge-index order), which is not
        /// necessarily ascending neighbor-index order. Every one of them is still in the frontier — no
        /// lower-indexed neighbor of <paramref name="vertex"/> can have been forgotten yet, since
        /// <paramref name="vertex"/> itself is one of its own not-yet-decided neighbors.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public IReadOnlyList<int> EarlierNeighborSlots(int vertex)
        {
            ValidateVertex(vertex);
            return _earlierNeighborSlotsView[vertex];
        }

        /// <summary>
        /// The slots freed right after <paramref name="vertex"/> is decided: <paramref name="vertex"/>'s
        /// own slot if every neighbor it has is at a lower index (or it has none), plus the slot of any
        /// other vertex whose highest-indexed neighbor is exactly <paramref name="vertex"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public IReadOnlyList<int> ForgottenSlots(int vertex)
        {
            ValidateVertex(vertex);
            return _forgottenSlotsView[vertex];
        }

        /// <summary>Converts a vertex index to the ZDD variable level (<c>level = VertexCount - vertex</c>).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="vertex"/> is outside <c>0 .. VertexCount - 1</c>.</exception>
        public int VertexToLevel(int vertex)
        {
            ValidateVertex(vertex);
            return Graph.VertexCount - vertex;
        }

        /// <summary>The inverse of <see cref="VertexToLevel"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is outside <c>1 .. VertexCount</c>.</exception>
        public int LevelToVertex(int level)
        {
            if (level < 1 || level > Graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, $"Must be in 1 .. {Graph.VertexCount}.");
            }

            return Graph.VertexCount - level;
        }

        private void ValidateVertex(int vertex)
        {
            if ((uint)vertex >= (uint)Graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(vertex), vertex, $"Must be in 0 .. {Graph.VertexCount - 1}.");
            }
        }
    }
}

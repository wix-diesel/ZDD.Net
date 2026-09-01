using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// Precomputes the frontier-method bookkeeping that every graph spec needs: which vertices a given
    /// edge introduces or forgets, how wide the frontier gets, and which state-array slot each vertex
    /// occupies while it is in the frontier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The frontier at edge <c>i</c> is the set of vertices that have appeared in edges <c>0 .. i</c> but
    /// still have an incident edge among <c>i .. EdgeCount - 1</c>; its size is the size of the state a
    /// frontier-method spec must carry, and <see cref="MaxFrontierSize"/> — the largest such size over all
    /// edges — sits on the exponent of the build's time and memory. Because it only needs <see cref="Graph"/>,
    /// not an actual spec or build, it doubles as the "estimate before you build" API called for by
    /// PLAN.md §13: construct a <see cref="FrontierManager"/> and read <see cref="MaxFrontierSize"/> before
    /// committing to a <see cref="Frontier.FrontierBuilder"/> run.
    /// </para>
    /// <para>
    /// Everything is precomputed once in the constructor in <c>O(VertexCount + EdgeCount)</c> and read from
    /// arrays afterwards, so it stays cheap to consult from inside a spec's <c>GetChild</c> hot path.
    /// </para>
    /// </remarks>
    public sealed class FrontierManager
    {
        private readonly int[] _firstEdge;
        private readonly int[] _lastEdge;
        private readonly int[] _slotOfVertex;
        private readonly int[][] _introducedByEdge;
        private readonly int[][] _forgottenByEdge;
        private readonly ReadOnlyCollection<int>[] _introducedByEdgeView;
        private readonly ReadOnlyCollection<int>[] _forgottenByEdgeView;
        private readonly int[] _frontierSizeByEdge;

        /// <summary>Precomputes the frontier bookkeeping for <paramref name="graph"/>'s edge order.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public FrontierManager(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            Graph = graph;
            int vertexCount = graph.VertexCount;
            int edgeCount = graph.EdgeCount;

            // First/last edge touching each vertex; -1 for an isolated vertex, which never enters the frontier.
            _firstEdge = new int[vertexCount];
            _lastEdge = new int[vertexCount];
            Array.Fill(_firstEdge, -1);
            Array.Fill(_lastEdge, -1);

            for (int i = 0; i < edgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);

                if (_firstEdge[edge.U] < 0)
                {
                    _firstEdge[edge.U] = i;
                }

                if (_firstEdge[edge.V] < 0)
                {
                    _firstEdge[edge.V] = i;
                }

                _lastEdge[edge.U] = i;
                _lastEdge[edge.V] = i;
            }

            // Bucket vertices by first/last edge, in a single counting pass (no per-edge lists to grow).
            var introducedCounts = new int[edgeCount];
            var forgottenCounts = new int[edgeCount];
            for (int v = 0; v < vertexCount; v++)
            {
                if (_firstEdge[v] >= 0)
                {
                    introducedCounts[_firstEdge[v]]++;
                }

                if (_lastEdge[v] >= 0)
                {
                    forgottenCounts[_lastEdge[v]]++;
                }
            }

            _introducedByEdge = new int[edgeCount][];
            _forgottenByEdge = new int[edgeCount][];
            _introducedByEdgeView = new ReadOnlyCollection<int>[edgeCount];
            _forgottenByEdgeView = new ReadOnlyCollection<int>[edgeCount];
            for (int i = 0; i < edgeCount; i++)
            {
                _introducedByEdge[i] = new int[introducedCounts[i]];
                _forgottenByEdge[i] = new int[forgottenCounts[i]];
                _introducedByEdgeView[i] = new ReadOnlyCollection<int>(_introducedByEdge[i]);
                _forgottenByEdgeView[i] = new ReadOnlyCollection<int>(_forgottenByEdge[i]);
            }

            var introducedFill = new int[edgeCount];
            var forgottenFill = new int[edgeCount];
            for (int v = 0; v < vertexCount; v++)
            {
                if (_firstEdge[v] >= 0)
                {
                    int i = _firstEdge[v];
                    _introducedByEdge[i][introducedFill[i]++] = v;
                }

                if (_lastEdge[v] >= 0)
                {
                    int i = _lastEdge[v];
                    _forgottenByEdge[i][forgottenFill[i]++] = v;
                }
            }

            // Slot assignment: on introduction, reuse the lowest-numbered slot freed by an already-forgotten
            // vertex, or allocate a fresh one; on forgetting (after the edge that forgets it), return the
            // slot to the free list. Because a new slot is only ever allocated when none is free, the total
            // number of distinct slots used equals the frontier's peak size.
            _slotOfVertex = new int[vertexCount];
            Array.Fill(_slotOfVertex, -1);
            _frontierSizeByEdge = new int[edgeCount];

            var freeSlots = new SortedSet<int>();
            int nextSlot = 0;
            int frontierCount = 0;

            for (int i = 0; i < edgeCount; i++)
            {
                foreach (int v in _introducedByEdge[i])
                {
                    int slot;
                    if (freeSlots.Count > 0)
                    {
                        slot = freeSlots.Min;
                        freeSlots.Remove(slot);
                    }
                    else
                    {
                        slot = nextSlot++;
                    }

                    _slotOfVertex[v] = slot;
                    frontierCount++;
                }

                _frontierSizeByEdge[i] = frontierCount;

                foreach (int v in _forgottenByEdge[i])
                {
                    freeSlots.Add(_slotOfVertex[v]);
                    frontierCount--;
                }
            }

            MaxFrontierSize = edgeCount == 0 ? 0 : System.Linq.Enumerable.Max(_frontierSizeByEdge);
        }

        /// <summary>The graph this instance was built from.</summary>
        public Graph Graph { get; }

        /// <summary>
        /// The largest frontier size over all edges — the state size a frontier-method spec on this graph's
        /// edge order must carry, and what its build's time and memory sit on the exponent of. Callable
        /// before any spec or build exists, so it doubles as the estimate PLAN.md §13 asks for.
        /// </summary>
        public int MaxFrontierSize { get; }

        /// <summary>
        /// The vertices that first appear at <paramref name="edgeIndex"/> (neither endpoint of an earlier
        /// edge), in ascending vertex order.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        public IReadOnlyList<int> IntroducedVertices(int edgeIndex)
        {
            ValidateEdgeIndex(edgeIndex);
            return _introducedByEdgeView[edgeIndex];
        }

        /// <summary>
        /// The vertices for which <paramref name="edgeIndex"/> is their last incident edge (they appear in
        /// no later edge), in ascending vertex order.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        public IReadOnlyList<int> ForgottenVertices(int edgeIndex)
        {
            ValidateEdgeIndex(edgeIndex);
            return _forgottenByEdgeView[edgeIndex];
        }

        /// <summary>
        /// The frontier size while <paramref name="edgeIndex"/> is being decided: the number of vertices
        /// that have appeared in edges <c>0 .. edgeIndex</c> but still have an incident edge among
        /// <c>edgeIndex .. EdgeCount - 1</c>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>.</exception>
        public int FrontierSize(int edgeIndex)
        {
            ValidateEdgeIndex(edgeIndex);
            return _frontierSizeByEdge[edgeIndex];
        }

        /// <summary>
        /// The state-array slot <paramref name="vertex"/> occupies while it is in the frontier. Stable for
        /// the vertex's whole time in the frontier (its slot is only reassigned to another vertex after it
        /// has been forgotten), and unique among vertices simultaneously in the frontier.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="edgeIndex"/> is outside <c>0 .. EdgeCount - 1</c>, or <paramref name="vertex"/> is
        /// outside <c>0 .. VertexCount - 1</c>.
        /// </exception>
        /// <exception cref="ArgumentException"><paramref name="vertex"/> is not in the frontier at <paramref name="edgeIndex"/>.</exception>
        public int MateIndex(int edgeIndex, int vertex)
        {
            ValidateEdgeIndex(edgeIndex);

            if ((uint)vertex >= (uint)Graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(vertex), vertex, $"Must be in 0 .. {Graph.VertexCount - 1}.");
            }

            int first = _firstEdge[vertex];
            int last = _lastEdge[vertex];
            if (first < 0 || edgeIndex < first || edgeIndex > last)
            {
                throw new ArgumentException(
                    $"Vertex {vertex} is not in the frontier at edge {edgeIndex}.",
                    nameof(vertex));
            }

            return _slotOfVertex[vertex];
        }

        private void ValidateEdgeIndex(int edgeIndex)
        {
            if ((uint)edgeIndex >= (uint)Graph.EdgeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(edgeIndex), edgeIndex, $"Must be in 0 .. {Graph.EdgeCount - 1}.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets in which every vertex <c>v</c>'s degree lies in <c>[lo[v], hi[v]]</c>.
    /// A general form that many other graph specs are a special case of: a matching is <c>[0, 1]</c>
    /// everywhere, a perfect matching is <c>[1, 1]</c> everywhere, an edge cover is <c>[1, ∞)</c>
    /// everywhere, and <c>[0, 2]</c> everywhere is the degree bound every disjoint union of simple paths
    /// and cycles satisfies — a superset of what <see cref="CycleSpec"/> accepts, since a plain degree
    /// count cannot tell a closed cycle from a still-open path (see the <c>[0, 2]</c> remark below).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: one running degree count per frontier vertex, held in the state slot
    /// <see cref="FrontierManager.MateIndex"/> assigns it. Because any edge that would push a vertex's
    /// degree past <c>hi[v]</c> is rejected on the spot, a live state's count never exceeds <c>hi[v]</c>,
    /// so the slot's value range is <c>0 .. hi[v]</c> — often just 2 or 3 bits, per M3-7's design note.
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices at degree 0, then — if the edge is taken —
    /// increment both endpoints' counts, rejecting outright if either now exceeds its <c>hi</c>. Either
    /// way (taken or not), one of each endpoint's remaining incident edges has just been decided, so this
    /// is also where the branch-and-bound cutoff applies: if an endpoint's count plus every incident edge
    /// still undecided could not reach its <c>lo</c>, the branch is pruned now rather than waiting for the
    /// endpoint to be forgotten. The two are the same check — a forgotten vertex has zero incident edges
    /// left, so "reject a forgotten vertex still below its <c>lo</c>" is just this pruning check applied
    /// one edge later, and both are handled by the single check below.
    /// </para>
    /// <para>
    /// <b>The <c>[0, 2]</c> / <see cref="CycleSpec"/> containment</b>: <c>[0, 2]</c> is a range, not the
    /// two-element set <c>{0, 2}</c> — it also allows degree 1, i.e. a dangling path endpoint — so
    /// <c>new DegreeConstraintSpec(graph, 0, 2)</c> accepts every disjoint union of simple paths
    /// <em>and</em> cycles (plus the empty edge set), a strict superset of
    /// <c>new CycleSpec(graph, single: false)</c> rather than that family plus one extra member. Degree
    /// bounds alone cannot pin the family down further: telling a closed cycle apart from an open path
    /// needs a "does this chain ever close" condition, which is exactly what <see cref="CycleSpec"/>'s
    /// mate-chain state (not a plain degree count) tracks. So the two are only ever in a subset
    /// relationship here, never equal — which is the point M3-7 asks this remark to document.
    /// </para>
    /// </remarks>
    public readonly struct DegreeConstraintSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int[] _lo;
        private readonly int[] _hi;

        // For edge i = (u, v): how many more edges incident to u (resp. v), other than edge i itself,
        // remain to be decided after edge i. Precomputed once so GetChild's pruning check is O(1).
        private readonly int[] _remainingAfterU;
        private readonly int[] _remainingAfterV;

        /// <summary>Creates a spec enforcing a per-vertex degree range on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="lo">The minimum degree for each vertex, indexed like <see cref="Graph.VertexCount"/>.</param>
        /// <param name="hi">The maximum degree for each vertex, indexed like <see cref="Graph.VertexCount"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/>, <paramref name="lo"/> or <paramref name="hi"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="lo"/> or <paramref name="hi"/> does not have exactly <see cref="Graph.VertexCount"/> entries,
        /// or some <c>hi[v]</c> is less than <c>lo[v]</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">Some <c>lo[v]</c> is negative.</exception>
        public DegreeConstraintSpec(Graph graph, int[] lo, int[] hi)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(lo);
            ArgumentNullException.ThrowIfNull(hi);

            if (lo.Length != graph.VertexCount)
            {
                throw new ArgumentException(
                    $"Expected {graph.VertexCount} entries (one per vertex), got {lo.Length}.", nameof(lo));
            }

            if (hi.Length != graph.VertexCount)
            {
                throw new ArgumentException(
                    $"Expected {graph.VertexCount} entries (one per vertex), got {hi.Length}.", nameof(hi));
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (lo[v] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(lo), lo[v], $"lo[{v}] must not be negative.");
                }

                if (hi[v] < lo[v])
                {
                    throw new ArgumentException(
                        $"hi[{v}] ({hi[v]}) must not be less than lo[{v}] ({lo[v]}).", nameof(hi));
                }
            }

            _graph = graph;
            _lo = (int[])lo.Clone();
            _hi = (int[])hi.Clone();
            _frontierManager = new FrontierManager(graph);

            int edgeCount = graph.EdgeCount;
            _remainingAfterU = new int[edgeCount];
            _remainingAfterV = new int[edgeCount];

            var occurrencesSoFar = new int[graph.VertexCount];
            for (int i = 0; i < edgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);
                _remainingAfterU[i] = graph.Degree(edge.U) - occurrencesSoFar[edge.U] - 1;
                _remainingAfterV[i] = graph.Degree(edge.V) - occurrencesSoFar[edge.V] - 1;
                occurrencesSoFar[edge.U]++;
                occurrencesSoFar[edge.V]++;
            }
        }

        /// <summary>Creates a spec enforcing the same <c>[lo, hi]</c> degree range on every vertex.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="lo">The minimum degree, applied to every vertex.</param>
        /// <param name="hi">The maximum degree, applied to every vertex.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="hi"/> is less than <paramref name="lo"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="lo"/> is negative.</exception>
        public DegreeConstraintSpec(Graph graph, int lo, int hi)
            : this(graph, Uniform(graph, lo), Uniform(graph, hi))
        {
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            for (int v = 0; v < _graph.VertexCount; v++)
            {
                if (_graph.Degree(v) == 0 && _lo[v] > 0)
                {
                    return DdResult.False; // an isolated vertex can never reach a positive lo[v]
                }
            }

            if (_graph.EdgeCount == 0)
            {
                // Every vertex is isolated (degree 0), and the loop above already confirmed lo[v] <= 0
                // for all of them; hi[v] >= lo[v] >= 0 holds by construction, so degree 0 satisfies both.
                return DdResult.True;
            }

            // state is zero-filled by the caller: every slot already reads degree 0.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                state[_frontierManager.MateIndex(edgeIndex, introducedVertices[i])] = 0;
            }

            int su = _frontierManager.MateIndex(edgeIndex, edge.U);
            int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

            if (value == 1)
            {
                if (++state[su] > _hi[edge.U] || ++state[sv] > _hi[edge.V])
                {
                    return DdResult.False;
                }
            }

            // Whether or not this edge was taken, one of u's and one of v's remaining candidate edges
            // has just been decided. If even taking every one still left cannot reach lo, prune now —
            // this is also what makes the exact check below unnecessary at a plain "forget" (a forgotten
            // vertex has 0 edges left, so it degenerates to state[slot] < lo[v] exactly).
            if (state[su] + _remainingAfterU[edgeIndex] < _lo[edge.U] ||
                state[sv] + _remainingAfterV[edgeIndex] < _lo[edge.V])
            {
                return DdResult.False;
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                // Clear so a slot a later vertex reuses never inherits a stale degree (IArrayDdSpec:
                // equality/hashing is element-wise, so leftovers would keep equivalent states from merging).
                state[_frontierManager.MateIndex(edgeIndex, forgottenVertices[i])] = 0;
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }

        private static int[] Uniform(Graph graph, int value)
        {
            ArgumentNullException.ThrowIfNull(graph);

            var array = new int[graph.VertexCount];
            Array.Fill(array, value);
            return array;
        }
    }
}

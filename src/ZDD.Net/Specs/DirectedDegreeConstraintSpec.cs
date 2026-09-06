using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of arc sets in which every vertex <c>v</c>'s in-degree lies in <c>[inLo[v], inHi[v]]</c>
    /// and out-degree lies in <c>[outLo[v], outHi[v]]</c>, independently of each other. The directed
    /// analogue of <see cref="DegreeConstraintSpec"/> (docs/design/m7-directed-graphs.md §3.4): same shape,
    /// except each frontier vertex's state is now (in-count, out-count) instead of a single count, because
    /// an arc <c>u -&gt; v</c> only ever moves <c>u</c>'s out-count and <c>v</c>'s in-count — never both
    /// counts of the same endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: two running counts per frontier vertex — in-count and out-count — held in the state
    /// slots <see cref="FrontierManager.MateIndex"/> assigns, one pair per slot. As with
    /// <see cref="DegreeConstraintSpec"/>, a live state's counts never exceed their <c>hi</c> bound (an arc
    /// that would push one past it is rejected on the spot).
    /// </para>
    /// <para>
    /// <b>Per arc <c>u -&gt; v</c></b>: introduce this arc's new vertices at (in 0, out 0), then — if the arc
    /// is taken — increment <c>u</c>'s out-count and <c>v</c>'s in-count, rejecting outright if either now
    /// exceeds its <c>hi</c>. Either way, one of <c>u</c>'s remaining outgoing arcs and one of <c>v</c>'s
    /// remaining incoming arcs has just been decided, so the same branch-and-bound cutoff
    /// <see cref="DegreeConstraintSpec"/> uses applies here per direction: if <c>u</c>'s out-count plus every
    /// outgoing arc still undecided could not reach <c>outLo[u]</c> (symmetrically for <c>v</c>'s in-count
    /// and <c>inLo[v]</c>), the branch is pruned now. Because this check fires at <em>every</em> arc where
    /// <c>u</c> is the source (respectively <c>v</c> is the target), it necessarily fires once more at each
    /// vertex's last arc in that direction — which happens no later than the vertex leaving the frontier —
    /// so no separate check is needed at forget time.
    /// </para>
    /// </remarks>
    public readonly struct DirectedDegreeConstraintSpec : IArrayDdSpec
    {
        private readonly DirectedGraph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int[] _inLo;
        private readonly int[] _inHi;
        private readonly int[] _outLo;
        private readonly int[] _outHi;

        // For arc i = (u, v): how many more arcs leaving u (resp. entering v), other than arc i itself,
        // remain to be decided after arc i. Precomputed once so GetChild's pruning check is O(1).
        private readonly int[] _remainingOutAfter;
        private readonly int[] _remainingInAfter;

        /// <summary>Creates a spec enforcing per-vertex in-/out-degree ranges on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="inLo">The minimum in-degree for each vertex, indexed like <see cref="DirectedGraph.VertexCount"/>.</param>
        /// <param name="inHi">The maximum in-degree for each vertex, indexed like <see cref="DirectedGraph.VertexCount"/>.</param>
        /// <param name="outLo">The minimum out-degree for each vertex, indexed like <see cref="DirectedGraph.VertexCount"/>.</param>
        /// <param name="outHi">The maximum out-degree for each vertex, indexed like <see cref="DirectedGraph.VertexCount"/>.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="graph"/>, <paramref name="inLo"/>, <paramref name="inHi"/>, <paramref name="outLo"/> or <paramref name="outHi"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Any array does not have exactly <see cref="DirectedGraph.VertexCount"/> entries, or some
        /// <c>inHi[v]</c>/<c>outHi[v]</c> is less than the matching <c>inLo[v]</c>/<c>outLo[v]</c>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">Some <c>inLo[v]</c> or <c>outLo[v]</c> is negative.</exception>
        public DirectedDegreeConstraintSpec(DirectedGraph graph, int[] inLo, int[] inHi, int[] outLo, int[] outHi)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(inLo);
            ArgumentNullException.ThrowIfNull(inHi);
            ArgumentNullException.ThrowIfNull(outLo);
            ArgumentNullException.ThrowIfNull(outHi);

            CheckLength(graph, inLo, nameof(inLo));
            CheckLength(graph, inHi, nameof(inHi));
            CheckLength(graph, outLo, nameof(outLo));
            CheckLength(graph, outHi, nameof(outHi));

            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (inLo[v] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(inLo), inLo[v], $"inLo[{v}] must not be negative.");
                }

                if (outLo[v] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(outLo), outLo[v], $"outLo[{v}] must not be negative.");
                }

                if (inHi[v] < inLo[v])
                {
                    throw new ArgumentException(
                        $"inHi[{v}] ({inHi[v]}) must not be less than inLo[{v}] ({inLo[v]}).", nameof(inHi));
                }

                if (outHi[v] < outLo[v])
                {
                    throw new ArgumentException(
                        $"outHi[{v}] ({outHi[v]}) must not be less than outLo[{v}] ({outLo[v]}).", nameof(outHi));
                }
            }

            _graph = graph;
            _inLo = (int[])inLo.Clone();
            _inHi = (int[])inHi.Clone();
            _outLo = (int[])outLo.Clone();
            _outHi = (int[])outHi.Clone();
            _frontierManager = new FrontierManager(graph);

            int edgeCount = graph.EdgeCount;
            _remainingOutAfter = new int[edgeCount];
            _remainingInAfter = new int[edgeCount];

            var outSoFar = new int[graph.VertexCount];
            var inSoFar = new int[graph.VertexCount];
            for (int i = 0; i < edgeCount; i++)
            {
                DirectedEdge arc = graph.GetEdge(i);
                _remainingOutAfter[i] = graph.OutDegree(arc.From) - outSoFar[arc.From] - 1;
                _remainingInAfter[i] = graph.InDegree(arc.To) - inSoFar[arc.To] - 1;
                outSoFar[arc.From]++;
                inSoFar[arc.To]++;
            }
        }

        /// <summary>Creates a spec enforcing the same in-/out-degree range on every vertex.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="inLo">The minimum in-degree, applied to every vertex.</param>
        /// <param name="inHi">The maximum in-degree, applied to every vertex.</param>
        /// <param name="outLo">The minimum out-degree, applied to every vertex.</param>
        /// <param name="outHi">The maximum out-degree, applied to every vertex.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="inHi"/> is less than <paramref name="inLo"/>, or <paramref name="outHi"/> is less than <paramref name="outLo"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="inLo"/> or <paramref name="outLo"/> is negative.</exception>
        public DirectedDegreeConstraintSpec(DirectedGraph graph, int inLo, int inHi, int outLo, int outHi)
            : this(graph, Uniform(graph, inLo), Uniform(graph, inHi), Uniform(graph, outLo), Uniform(graph, outHi))
        {
        }

        /// <summary>The graph this spec searches.</summary>
        public DirectedGraph Graph => _graph;

        /// <summary>The in-count slot paired with mate slot <paramref name="mateSlot"/>.</summary>
        private int InSlot(int mateSlot) => mateSlot;

        /// <summary>The out-count slot paired with mate slot <paramref name="mateSlot"/>.</summary>
        private int OutSlot(int mateSlot) => _frontierManager.MaxFrontierSize + mateSlot;

        /// <inheritdoc/>
        public int ArrayLength => 2 * _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            for (int v = 0; v < _graph.VertexCount; v++)
            {
                if (_graph.OutDegree(v) == 0 && _outLo[v] > 0)
                {
                    return DdResult.False; // a vertex with no outgoing arcs can never reach a positive outLo[v]
                }

                if (_graph.InDegree(v) == 0 && _inLo[v] > 0)
                {
                    return DdResult.False; // a vertex with no incoming arcs can never reach a positive inLo[v]
                }
            }

            if (_graph.EdgeCount == 0)
            {
                // Every vertex has in-degree and out-degree 0, and the loop above already confirmed both
                // lo bounds are <= 0 for all of them; hi >= lo >= 0 holds by construction, so this satisfies both.
                return DdResult.True;
            }

            // state is zero-filled by the caller: every in/out slot already reads degree 0.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            DirectedEdge arc = _graph.GetEdge(edgeIndex);

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, introducedVertices[i]);
                state[InSlot(slot)] = 0;
                state[OutSlot(slot)] = 0;
            }

            int su = _frontierManager.MateIndex(edgeIndex, arc.From);
            int sv = _frontierManager.MateIndex(edgeIndex, arc.To);
            int outU = OutSlot(su);
            int inV = InSlot(sv);

            if (value == 1)
            {
                if (++state[outU] > _outHi[arc.From] || ++state[inV] > _inHi[arc.To])
                {
                    return DdResult.False;
                }
            }

            // See the class remarks: this is the only pruning check the out-direction (resp. in-direction)
            // ever needs, since it fires again at u's (resp. v's) very last arc in that direction.
            if (state[outU] + _remainingOutAfter[edgeIndex] < _outLo[arc.From] ||
                state[inV] + _remainingInAfter[edgeIndex] < _inLo[arc.To])
            {
                return DdResult.False;
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                // Clear so a slot a later vertex reuses never inherits a stale count (IArrayDdSpec:
                // equality/hashing is element-wise, so leftovers would keep equivalent states from merging).
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                state[InSlot(slot)] = 0;
                state[OutSlot(slot)] = 0;
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }

        private static void CheckLength(DirectedGraph graph, int[] array, string paramName)
        {
            if (array.Length != graph.VertexCount)
            {
                throw new ArgumentException(
                    $"Expected {graph.VertexCount} entries (one per vertex), got {array.Length}.", paramName);
            }
        }

        private static int[] Uniform(DirectedGraph graph, int value)
        {
            ArgumentNullException.ThrowIfNull(graph);

            var array = new int[graph.VertexCount];
            Array.Fill(array, value);
            return array;
        }
    }
}

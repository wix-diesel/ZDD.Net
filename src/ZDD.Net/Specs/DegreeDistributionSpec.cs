using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets whose degree sequence matches a required histogram exactly: for every
    /// degree <c>d</c>, the number of vertices whose final degree is <c>d</c> equals <c>counts[d]</c>.
    /// Graphillion's <c>degree_distribution_graphs</c>. A <c>k</c>-regular graph is the special case
    /// <c>counts[k] == VertexCount</c> (every other entry zero) — equivalently
    /// <see cref="DegreeConstraintSpec"/> with <c>lo</c> and <c>hi</c> both <c>k</c> everywhere, which is
    /// why no separate spec exists for that case (see <see cref="Graphs.GraphSet.RegularGraphs"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: one running degree count per frontier vertex (as in <see cref="DegreeConstraintSpec"/>),
    /// plus <c>counts.Length</c> further slots — past the frontier's own — holding, for each degree
    /// <c>d</c>, how many more vertices still need to end up with that degree: the "remaining histogram".
    /// Holding the confirmed vertices' actual degree distribution directly would blow up the state (it would
    /// have to distinguish every way the confirmed vertices could split across degrees); counting down from
    /// <c>counts[d]</c> instead needs only <c>counts.Length</c> extra scalars; total.
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices at degree 0, as usual. If the edge is taken,
    /// increment both endpoints' running degree — rejecting outright if either now exceeds
    /// <c>counts.Length - 1</c>, the highest degree any bucket exists for (this is also what keeps every
    /// later histogram-slot access in range: a running degree can never leave <c>0 .. counts.Length - 1</c>).
    /// When an edge forgets a vertex, that vertex's degree is final: decrement
    /// <c>remaining[finalDegree]</c>, and reject if it goes negative &#8212; more vertices have now finished
    /// at that degree than the histogram asked for.
    /// </para>
    /// <para>
    /// <b>Acceptance</b>: once every edge is decided, every vertex has been forgotten and every
    /// <c>remaining[d]</c> must read exactly <c>0</c> &#8212; the histogram was consumed exactly, not just
    /// never driven negative. This is checked once, at the final edge, rather than incrementally, since a
    /// slot can still be nonzero right up until the very last vertex that could have retired it is decided.
    /// </para>
    /// <para>
    /// <b><c>counts</c> not summing to <see cref="Graphs.Graph.VertexCount"/></b>: no edge set can possibly
    /// satisfy such a histogram (every vertex has exactly one final degree, so the counts must partition all
    /// of them), so this spec builds the empty family rather than throwing &#8212; the same choice
    /// <see cref="KnapsackSpec"/> makes for a negative capacity, another case where the input describes a
    /// family that is simply always empty rather than a malformed request.
    /// </para>
    /// </remarks>
    public readonly struct DegreeDistributionSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int[] _counts;

        /// <summary>Creates a spec enforcing an exact degree histogram on <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="counts">
        /// The required number of vertices at each degree: <c>counts[d]</c> vertices must end up with
        /// degree exactly <c>d</c>. Copied, so later mutating the array passed in has no effect on the
        /// spec. A degree of <c>counts.Length</c> or higher is never accepted (there is no bucket for it).
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> or <paramref name="counts"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Some <c>counts[d]</c> is negative.</exception>
        public DegreeDistributionSpec(Graph graph, int[] counts)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(counts);

            for (int d = 0; d < counts.Length; d++)
            {
                if (counts[d] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(counts), counts[d], $"counts[{d}] must not be negative.");
                }
            }

            _graph = graph;
            _counts = (int[])counts.Clone();
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>The required degree histogram: <see cref="Counts"/>[d] vertices must have degree exactly d.</summary>
        public IReadOnlyList<int> Counts => _counts;

        /// <summary>The highest degree any histogram bucket exists for: <c>Counts.Count - 1</c>.</summary>
        private int MaxDegree => _counts.Length - 1;

        /// <summary>Where the remaining-histogram slots start: right after the frontier's own slots.</summary>
        private int HistogramBase => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize + _counts.Length;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            long sum = 0;
            for (int d = 0; d < _counts.Length; d++)
            {
                sum += _counts[d];
                state[HistogramBase + d] = _counts[d];
            }

            if (sum != _graph.VertexCount)
            {
                // The counts cannot possibly partition every vertex: no edge set can ever satisfy them.
                return DdResult.False;
            }

            for (int v = 0; v < _graph.VertexCount; v++)
            {
                if (_graph.Degree(v) == 0 && !TryRetire(state, degree: 0))
                {
                    return DdResult.False; // an isolated vertex's degree-0 already exhausted the histogram
                }
            }

            if (_graph.EdgeCount == 0)
            {
                // Every vertex is isolated and already retired above; accept only if that alone
                // exhausted the whole histogram (every required degree was 0).
                return IsHistogramExhausted(state) ? DdResult.True : DdResult.False;
            }

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

            if (value == 1)
            {
                int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

                if (++state[su] > MaxDegree || ++state[sv] > MaxDegree)
                {
                    return DdResult.False;
                }
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                int finalDegree = state[slot];

                if (!TryRetire(state, finalDegree))
                {
                    return DdResult.False;
                }

                // Clear so a slot a later vertex reuses never inherits a stale degree (IArrayDdSpec:
                // equality/hashing is element-wise, so leftovers would keep equivalent states from merging).
                state[slot] = 0;
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : (IsHistogramExhausted(state) ? DdResult.True : DdResult.False);
        }

        // Decrements the remaining count for a vertex finishing at `degree`; false if that would go
        // negative (more vertices retired at this degree than the histogram allows).
        private bool TryRetire(Span<int> state, int degree)
        {
            int slot = HistogramBase + degree;
            state[slot]--;
            return state[slot] >= 0;
        }

        private bool IsHistogramExhausted(Span<int> state)
        {
            for (int d = 0; d < _counts.Length; d++)
            {
                if (state[HistogramBase + d] != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets that form a matching of a graph: no two chosen edges share an endpoint.
    /// With <see cref="Perfect"/> set, only matchings that cover every vertex are kept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: one flag per frontier vertex, held in the state slot <see cref="FrontierManager.MateIndex"/>
    /// assigns it — <see cref="SlotUnmatched"/> or <see cref="SlotMatched"/>. Where <see cref="PathSpec"/> tracks
    /// each frontier vertex's chain partner via a <c>mate</c> array and <see cref="SpanningTreeSpec"/> tracks
    /// which connected component it belongs to via a <c>comp</c> array, this spec only needs a single bit per
    /// vertex — the third pattern (a per-vertex flag), of which the (not yet implemented)
    /// <c>DegreeConstraintSpec</c> is the general form.
    /// </para>
    /// <para>
    /// <b>Per edge</b>: introduce this edge's new vertices as <see cref="SlotUnmatched"/>, then — if the edge
    /// is taken — reject it if either endpoint is already <see cref="SlotMatched"/>, otherwise mark both
    /// endpoints matched. Finally, for each vertex this edge forgets: with <see cref="Perfect"/> set, reject
    /// if it is still <see cref="SlotUnmatched"/> (it can never be matched later, since it has no more
    /// incident edges); its slot is then reset to <see cref="SlotUnmatched"/> so a later vertex reusing the
    /// slot never inherits a stale flag (see <see cref="IArrayDdSpec"/>'s remark on clearing slots that no
    /// longer matter).
    /// </para>
    /// </remarks>
    public readonly struct MatchingSpec : IArrayDdSpec
    {
        /// <summary>The vertex has no matched edge (yet, or ever).</summary>
        private const int SlotUnmatched = 0;

        /// <summary>The vertex already has a matched edge; no further edge may touch it.</summary>
        private const int SlotMatched = 1;

        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly bool _perfect;

        /// <summary>Creates a spec for matchings of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="perfect">
        /// When <see langword="true"/>, only matchings that cover every vertex are kept; when
        /// <see langword="false"/> (the default), every matching — including the empty one — is kept.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public MatchingSpec(Graph graph, bool perfect = false)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _graph = graph;
            _perfect = perfect;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>Whether only matchings that cover every vertex are kept.</summary>
        public bool Perfect => _perfect;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_perfect && (_graph.VertexCount & 1) != 0)
            {
                return DdResult.False; // an odd number of vertices can never be perfectly covered
            }

            if (_graph.EdgeCount == 0)
            {
                // No edge can ever be taken: the only possible matching is the empty one, which is
                // perfect only when there is nothing to cover (impossible, since VertexCount >= 1).
                return _perfect ? DdResult.False : DdResult.True;
            }

            if (_perfect)
            {
                for (int v = 0; v < _graph.VertexCount; v++)
                {
                    if (_graph.Degree(v) == 0)
                    {
                        return DdResult.False; // an isolated vertex can never be matched
                    }
                }
            }

            // state is zero-filled by the caller: every slot already reads SlotUnmatched.
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
                state[_frontierManager.MateIndex(edgeIndex, introducedVertices[i])] = SlotUnmatched;
            }

            if (value == 1)
            {
                int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                int sv = _frontierManager.MateIndex(edgeIndex, edge.V);
                if (state[su] == SlotMatched || state[sv] == SlotMatched)
                {
                    return DdResult.False; // one endpoint is already matched
                }

                state[su] = SlotMatched;
                state[sv] = SlotMatched;
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                if (_perfect && state[slot] == SlotUnmatched)
                {
                    return DdResult.False; // this vertex has no more incident edges and is still unmatched
                }

                state[slot] = SlotUnmatched; // clear so a reused slot never carries a stale matched flag
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }
}

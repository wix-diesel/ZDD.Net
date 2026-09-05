using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of edge sets that form a complete bipartite subgraph (a "biclique") of a graph &#8212;
    /// Graphillion's <c>bicliques</c>. Every vertex ends up on one of the biclique's two sides, or unused;
    /// the family requires every edge between the two sides to be present and selected, and no other edge
    /// selected at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The empty edge set is a member</b> (the trivial, zero-vertex-on-each-side biclique): with no
    /// edges taken, every vertex ends up unused, which trivially satisfies "every cross edge present" (there
    /// are none) and "no other edge selected" (also none). This matches how <see cref="CliqueSpec"/> and
    /// <see cref="IndependentSetSpec"/> both include the empty vertex set as a trivial member of their own
    /// families. <see cref="BicliqueSpec(Graph, int, int)"/>'s size-fixed overload only reaches the empty set
    /// when both sizes are <c>0</c>.
    /// </para>
    /// <para>
    /// <b>State</b>: a <see cref="BicliqueVertexState"/> parity-union-find code per frontier vertex (which
    /// group it belongs to and which of the group's two relative sides it is on — see that type's remarks
    /// for why a single global "SideA"/"SideB" label cannot work), a parallel slot per frontier vertex
    /// recording which graph vertex currently occupies it, one forgotten-side-flags slot and (for the
    /// size-fixed overload) two running side-count slots per <i>representative</i> slot, a global count of
    /// currently-distinct groups, and — size-fixed only — two global running totals that absorb a group's
    /// final counts once it fully dissolves (its last member forgotten), since no frontier slot survives to
    /// keep holding them.
    /// </para>
    /// <para>
    /// <b>Per edge</b> <c>(u, v)</c>, when taken: either endpoint still <see cref="BicliqueVertexState.Free"/>
    /// first becomes its own new single-member group, then <see cref="BicliqueVertexState.TryMerge"/> forces
    /// <c>u</c> and <c>v</c> onto opposite sides of what becomes one group (rejecting if that is impossible —
    /// see that method's remarks), and the distinct-group count drops by one whenever this actually joins two
    /// previously-separate groups. When not taken: reject only if <c>u</c> and <c>v</c> are already in the
    /// <i>same</i> group and already on different sides (a complete bipartite graph would have to include
    /// this edge); otherwise the decision is deferred exactly as <see cref="InducedSubgraphSpec"/> defers its
    /// own not-taken case.
    /// </para>
    /// <para>
    /// <b>Connectivity</b>: a non-empty biclique is one connected piece, so the family only accepts branches
    /// that end with at most one group ever having existed at once — tracked simply as the distinct-group
    /// count reaching <c>0</c> (no edge ever taken) or <c>1</c> (exactly one connected group) once the last
    /// edge is decided, rather than the state needing to remember which specific groups those were.
    /// </para>
    /// <para>
    /// <b>Size-fixed overload</b>: <see cref="BicliqueSpec(Graph, int, int)"/> additionally caps each group's
    /// running side counts at <c>Math.Max(a, b)</c>, rejecting a merge that would exceed it — a smaller state
    /// and a narrower frontier than the unconstrained form, since far fewer size combinations stay reachable.
    /// Because which physical side a run happens to label relative side <c>0</c> versus <c>1</c> is an
    /// arbitrary, deterministic accident of edge order (a biclique's two sides are not otherwise
    /// distinguishable), the final totals are accepted in <i>either</i> assignment: <c>(a, b)</c> or
    /// <c>(b, a)</c>.
    /// </para>
    /// </remarks>
    public readonly struct BicliqueSpec : IArrayDdSpec
    {
        private readonly Graph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly Dictionary<Edge, int> _edgeIndexOf;
        private readonly bool _sizeFixed;
        private readonly int _a;
        private readonly int _b;

        /// <summary>Creates a spec for every complete bipartite subgraph of <paramref name="graph"/>, of any size.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        public BicliqueSpec(Graph graph)
        {
            ArgumentNullException.ThrowIfNull(graph);

            _graph = graph;
            _frontierManager = new FrontierManager(graph);
            _edgeIndexOf = BuildEdgeIndex(graph);
            _sizeFixed = false;
            _a = 0;
            _b = 0;
        }

        /// <summary>Creates a spec for complete bipartite subgraphs with sides of exactly <paramref name="a"/> and <paramref name="b"/> vertices.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="a">The required size of one side.</param>
        /// <param name="b">The required size of the other side.</param>
        /// <remarks>The two sides are interchangeable: a member is accepted whether its sides come out <c>(a, b)</c> or <c>(b, a)</c>.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="a"/> or <paramref name="b"/> is negative.</exception>
        public BicliqueSpec(Graph graph, int a, int b)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (a < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(a), a, "Must not be negative.");
            }

            if (b < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(b), b, "Must not be negative.");
            }

            _graph = graph;
            _frontierManager = new FrontierManager(graph);
            _edgeIndexOf = BuildEdgeIndex(graph);
            _sizeFixed = true;
            _a = a;
            _b = b;
        }

        /// <summary>The graph this spec searches.</summary>
        public Graph Graph => _graph;

        /// <summary>Whether this spec requires a fixed pair of side sizes (the <see cref="BicliqueSpec(Graph, int, int)"/> overload).</summary>
        public bool IsSizeFixed => _sizeFixed;

        /// <summary>One required side size, when <see cref="IsSizeFixed"/>; otherwise <c>0</c>.</summary>
        public int A => _a;

        /// <summary>The other required side size, when <see cref="IsSizeFixed"/>; otherwise <c>0</c>.</summary>
        public int B => _b;

        private int FrontierLength => _frontierManager.MaxFrontierSize;

        private int VertexOffset => FrontierLength;

        private int FlagsOffset => 2 * FrontierLength;

        private int CountAOffset => 3 * FrontierLength;

        private int CountBOffset => 4 * FrontierLength;

        private int DistinctGroupCountSlot => _sizeFixed ? 5 * FrontierLength : 3 * FrontierLength;

        private int GlobalCountASlot => (5 * FrontierLength) + 1;

        private int GlobalCountBSlot => (5 * FrontierLength) + 2;

        /// <inheritdoc/>
        public int ArrayLength => _sizeFixed ? (5 * FrontierLength) + 3 : (3 * FrontierLength) + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.EdgeCount == 0)
            {
                // No edges to decide: the only possible member is the empty edge set, valid iff the
                // size-fixed overload (if any) asks for two empty sides.
                bool emptyIsValid = !_sizeFixed || (_a == 0 && _b == 0);
                return emptyIsValid ? DdResult.True : DdResult.False;
            }

            // state is zero-filled by the caller: every code slot already reads BicliqueVertexState.Free
            // and every other slot (vertex-of, flags, counts, distinct-group count) already reads 0.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int frontierLength = FrontierLength;
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            Edge edge = _graph.GetEdge(edgeIndex);

            Span<int> code = state.Slice(0, frontierLength);
            Span<int> vertexOfSlot = state.Slice(VertexOffset, frontierLength);
            Span<int> flags = state.Slice(FlagsOffset, frontierLength);
            Span<int> countA = _sizeFixed ? state.Slice(CountAOffset, frontierLength) : default;
            Span<int> countB = _sizeFixed ? state.Slice(CountBOffset, frontierLength) : default;

            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                int vertex = introducedVertices[i];
                vertexOfSlot[_frontierManager.MateIndex(edgeIndex, vertex)] = vertex;
            }

            int su = _frontierManager.MateIndex(edgeIndex, edge.U);
            int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

            if (value == 1)
            {
                int distinctGroupCount = state[DistinctGroupCountSlot];

                if (!BicliqueVertexState.IsGrouped(code, su))
                {
                    BicliqueVertexState.CreateSingleton(code, flags, countA, countB, su);
                    distinctGroupCount++;
                }

                if (!BicliqueVertexState.IsGrouped(code, sv))
                {
                    BicliqueVertexState.CreateSingleton(code, flags, countA, countB, sv);
                    distinctGroupCount++;
                }

                int maxSideSize = _sizeFixed ? Math.Max(_a, _b) : int.MaxValue;
                if (!BicliqueVertexState.TryMerge(
                        code, vertexOfSlot, flags, countA, countB, frontierLength, su, sv, edgeIndex, _edgeIndexOf,
                        maxSideSize, out bool groupsReduced))
                {
                    return DdResult.False;
                }

                if (groupsReduced)
                {
                    distinctGroupCount--;
                }

                state[DistinctGroupCountSlot] = distinctGroupCount;
            }
            else if (BicliqueVertexState.IsGrouped(code, su) && BicliqueVertexState.IsGrouped(code, sv) &&
                     BicliqueVertexState.Representative(code, su) == BicliqueVertexState.Representative(code, sv) &&
                     BicliqueVertexState.RelativeSide(code, su) != BicliqueVertexState.RelativeSide(code, sv))
            {
                return DdResult.False; // a complete bipartite graph would have had to include this cross edge
            }

            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                int slot = _frontierManager.MateIndex(edgeIndex, forgottenVertices[i]);
                BicliqueVertexState.Forget(
                    code, flags, countA, countB, frontierLength, slot,
                    out bool dissolved, out int dissolvedCountA, out int dissolvedCountB);

                if (dissolved && _sizeFixed)
                {
                    state[GlobalCountASlot] += dissolvedCountA;
                    state[GlobalCountBSlot] += dissolvedCountB;
                }

                vertexOfSlot[slot] = 0;
            }

            int remaining = level - 1;
            if (remaining > 0)
            {
                return remaining;
            }

            if (state[DistinctGroupCountSlot] > 1)
            {
                return DdResult.False; // more than one connected piece: not a single biclique
            }

            if (_sizeFixed)
            {
                int countSideA = state[GlobalCountASlot];
                int countSideB = state[GlobalCountBSlot];
                bool matches = (countSideA == _a && countSideB == _b) || (countSideA == _b && countSideB == _a);
                return matches ? DdResult.True : DdResult.False;
            }

            return DdResult.True;
        }

        private static Dictionary<Edge, int> BuildEdgeIndex(Graph graph)
        {
            var edgeIndexOf = new Dictionary<Edge, int>(graph.EdgeCount);
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                edgeIndexOf[graph.GetEdge(i)] = i;
            }

            return edgeIndexOf;
        }
    }
}

using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of arc sets that form a rooted out-arborescence at <see cref="Root"/>: a directed tree in
    /// which every arc points away from the root, so <see cref="Root"/> can reach every vertex the tree
    /// touches by following a unique directed path. With <see cref="Spanning"/> (the default), the tree must
    /// touch every vertex of the graph; otherwise it may touch any subset that includes <see cref="Root"/>
    /// (docs/design/m7-directed-graphs.md §3.4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The decomposition</b>: an arborescence is exactly "a tree, as an undirected graph" plus "every
    /// non-root touched vertex has in-degree 1, and <see cref="Root"/> has in-degree 0" — the design's own
    /// phrasing. The first half reuses <see cref="SpanningComponentState"/>'s comp array unmodified (the same
    /// mechanics <see cref="SpanningTreeSpec"/> is built on); the second half needs only a one-bit
    /// "does this vertex already have a parent" flag per frontier vertex, since the bound is a fixed 0 or 1
    /// rather than a general range (contrast <see cref="DirectedDegreeConstraintSpec"/>, which does need
    /// running counts and a "remaining" pruning check for arbitrary bounds).
    /// </para>
    /// <para>
    /// <b>Why the decomposition needs no separate reachability check</b>: given the tree is connected,
    /// acyclic and has exactly <c>(size - 1)</c> edges, and every vertex's in-degree is capped at 1, the sum
    /// of in-degrees over the tree (<c>size - 1</c>) forces <em>exactly one</em> vertex to have in-degree 0 —
    /// simple counting, independent of which vertex that happens to be. Forcing <see cref="Root"/>'s in-degree
    /// to 0 outright therefore makes it that one vertex, and forces every other touched vertex to in-degree
    /// exactly 1 as a consequence, with no extra bookkeeping. It also means an arc into <see cref="Root"/> is
    /// never selectable (rejected outright), and a graph where some vertex is unreachable from
    /// <see cref="Root"/> can never complete a <see cref="Spanning"/> tree — the same counting argument runs
    /// in reverse: tracing any touched vertex's unique parent arc backwards, edge by edge, can only end at the
    /// tree's one in-degree-0 vertex, which is <see cref="Root"/>.
    /// </para>
    /// <para>
    /// <b><see cref="Spanning"/> mode</b>: identical to <see cref="SpanningTreeSpec"/>'s own rules — a
    /// component closing before the very last edge, or a second component closing at all, is rejected — plus
    /// the in-degree checks above layered onto every arc.
    /// </para>
    /// <para>
    /// <b>Non-<see cref="Spanning"/> mode</b>: relaxes connectivity the way <see cref="ForestSpec"/> relaxes
    /// <see cref="SpanningTreeSpec"/> — any number of components may close, at any time — but a closing
    /// component that has ever gained an arc (a real tree, not just an untouched vertex) must contain
    /// <see cref="Root"/>, or it would be a second, disconnected arborescence rather than one rooted at
    /// <see cref="Root"/>. <see cref="ArborescenceComponentState"/> tracks the two extra per-representative
    /// bits ("has gained an arc" and "contains <see cref="Root"/>") this needs, alongside the same comp array.
    /// The same counting argument above then applies within whichever single real tree survives, so no
    /// further per-vertex checks are needed there either.
    /// </para>
    /// </remarks>
    public readonly struct ArborescenceSpec : IArrayDdSpec
    {
        private readonly DirectedGraph _graph;
        private readonly FrontierManager _frontierManager;
        private readonly int _root;
        private readonly bool _spanning;

        /// <summary>Creates a spec for out-arborescences of <paramref name="graph"/> rooted at <paramref name="root"/>.</summary>
        /// <param name="graph">The graph to search.</param>
        /// <param name="root">The arborescence's root: the one vertex required to have in-degree 0.</param>
        /// <param name="spanning">
        /// When <see langword="true"/> (the default), the tree must touch every vertex of <paramref name="graph"/>.
        /// When <see langword="false"/>, it may touch any subset that includes <paramref name="root"/> —
        /// including the empty arc set, <paramref name="root"/> alone.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="root"/> is outside <c>0 .. graph.VertexCount - 1</c>.</exception>
        public ArborescenceSpec(DirectedGraph graph, int root, bool spanning = true)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if ((uint)root >= (uint)graph.VertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(root), root, $"Must be in 0 .. {graph.VertexCount - 1}.");
            }

            _graph = graph;
            _root = root;
            _spanning = spanning;
            _frontierManager = new FrontierManager(graph);
        }

        /// <summary>The graph this spec searches.</summary>
        public DirectedGraph Graph => _graph;

        /// <summary>The arborescence's root: the one vertex required to have in-degree 0.</summary>
        public int Root => _root;

        /// <summary>Whether the tree is required to touch every vertex of <see cref="Graph"/>.</summary>
        public bool Spanning => _spanning;

        private int FrontierLength => _frontierManager.MaxFrontierSize;

        /// <summary>The in-degree flag slot (0 or 1: "already has a parent") paired with mate slot <paramref name="mateSlot"/>.</summary>
        private int InDegreeSlot(int mateSlot) => FrontierLength + mateSlot;

        /// <summary>
        /// The base of the <c>hasRoot</c> bit array <see cref="ArborescenceComponentState"/> needs, used only
        /// when <see cref="Spanning"/> is <see langword="false"/>.
        /// </summary>
        private int HasRootBase => 2 * FrontierLength;

        /// <summary>The base of the <c>hasEdge</c> bit array; see <see cref="HasRootBase"/>.</summary>
        private int HasEdgeBase => 3 * FrontierLength;

        /// <summary>
        /// The closed-component-counter slot used only when <see cref="Spanning"/> is <see langword="true"/>
        /// — one past the last comp slot, exactly as <see cref="SpanningTreeSpec"/> uses it.
        /// </summary>
        private int ClosedCountSlot => 2 * FrontierLength;

        /// <inheritdoc/>
        public int ArrayLength => _spanning ? (2 * FrontierLength) + 1 : 4 * FrontierLength;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            if (_graph.VertexCount == 1)
            {
                return DdResult.True; // the root alone is trivially its own (edge-less) arborescence
            }

            if (_spanning)
            {
                if (_graph.EdgeCount == 0)
                {
                    return DdResult.False; // more than one vertex, no arcs at all: can never reach everyone
                }

                for (int v = 0; v < _graph.VertexCount; v++)
                {
                    if (_graph.OutDegree(v) + _graph.InDegree(v) == 0)
                    {
                        return DdResult.False; // an arc-less vertex can never join any tree
                    }
                }
            }
            else if (_graph.EdgeCount == 0)
            {
                return DdResult.True; // no arcs at all: the empty arc set (root alone) is always valid here
            }

            // state is zero-filled by the caller: every comp/bit slot already reads its empty default.
            return _graph.EdgeCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int edgeIndex = _graph.LevelToEdgeIndex(level);
            DirectedEdge arc = _graph.GetEdge(edgeIndex);
            int frontierLength = FrontierLength;

            // Indexed access rather than foreach: see PathSpec.GetChild for why (avoids boxing the
            // IReadOnlyList<int> enumerator on every call).
            IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
            for (int i = 0; i < introducedVertices.Count; i++)
            {
                int v = introducedVertices[i];
                int slot = _frontierManager.MateIndex(edgeIndex, v);
                state[InDegreeSlot(slot)] = 0;

                if (_spanning)
                {
                    SpanningComponentState.Introduce(state, slot);
                }
                else
                {
                    ArborescenceComponentState.Introduce(
                        state, state.Slice(HasRootBase, frontierLength), state.Slice(HasEdgeBase, frontierLength),
                        slot, v == _root);
                }
            }

            if (value == 1 && !TakeArc(state, edgeIndex, arc, frontierLength))
            {
                return DdResult.False;
            }

            bool isFinalEdge = level == 1;
            IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
            for (int i = 0; i < forgottenVertices.Count; i++)
            {
                if (!Forget(state, forgottenVertices[i], edgeIndex, frontierLength, isFinalEdge))
                {
                    return DdResult.False;
                }
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }

        /// <summary>Attempts to take arc <c>u -&gt; v</c>, merging its endpoints' components and marking <c>v</c> parented.</summary>
        /// <returns>
        /// <see langword="false"/> if the arc cannot be taken: it enters <see cref="Root"/>, <c>v</c> already
        /// has a parent, or <c>u</c> and <c>v</c> already share a component (this arc would close a cycle).
        /// </returns>
        private bool TakeArc(Span<int> state, int edgeIndex, DirectedEdge arc, int frontierLength)
        {
            int u = arc.From;
            int v = arc.To;

            if (v == _root)
            {
                return false; // the root never has an incoming arc
            }

            int su = _frontierManager.MateIndex(edgeIndex, u);
            int sv = _frontierManager.MateIndex(edgeIndex, v);
            int inV = InDegreeSlot(sv);

            if (state[inV] == 1)
            {
                return false; // v already has a parent: an arborescence gives every non-root vertex exactly one
            }

            bool merged = _spanning
                ? SpanningComponentState.TryMerge(state, frontierLength, su, sv)
                : ArborescenceComponentState.TryMerge(
                    state, state.Slice(HasRootBase, frontierLength), state.Slice(HasEdgeBase, frontierLength),
                    frontierLength, su, sv);

            if (!merged)
            {
                return false; // u and v already share a component: this arc would close a cycle
            }

            state[inV] = 1;
            return true;
        }

        /// <summary>Validates and retires <paramref name="vertex"/>, which this arc forgets.</summary>
        /// <returns><see langword="false"/> if its departure (or its component's closing) makes the family it belongs to invalid.</returns>
        private bool Forget(Span<int> state, int vertex, int edgeIndex, int frontierLength, bool isFinalEdge)
        {
            int slot = _frontierManager.MateIndex(edgeIndex, vertex);

            // Cleared regardless of mode: a stale in-degree flag on a slot a later vertex reuses would keep
            // otherwise-equivalent states from merging (IArrayDdSpec's element-wise equality).
            state[InDegreeSlot(slot)] = 0;

            if (_spanning)
            {
                if (!SpanningComponentState.Forget(state, frontierLength, slot))
                {
                    return true;
                }

                if (!isFinalEdge)
                {
                    return false; // closed too early: this component can never reach the rest of the graph
                }

                int closedCount = state[ClosedCountSlot] + 1;
                if (closedCount > 1)
                {
                    return false; // a second component finishing here means two trees, not one
                }

                state[ClosedCountSlot] = closedCount;
                return true;
            }

            bool closed = ArborescenceComponentState.Forget(
                state, state.Slice(HasRootBase, frontierLength), state.Slice(HasEdgeBase, frontierLength),
                frontierLength, slot, out bool closingHasRoot, out bool closingHasEdge);

            // An untouched singleton (never gained an arc) is always a fine thing to close, root or not; a
            // real tree (has gained an arc) must be the one rooted at Root, or it is a second, disconnected
            // arborescence rather than one rooted at Root.
            return !closed || !closingHasEdge || closingHasRoot;
        }
    }
}

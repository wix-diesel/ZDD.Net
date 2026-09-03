using System;
using System.Collections.Generic;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of proper <c>k</c>-colorings of a graph: assignments of one color, out of <c>k</c>, to
    /// every vertex such that no edge joins two same-colored vertices.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Variables are vertex &#215; color pairs</b>, not vertices and not edges — a departure from every
    /// other spec in this namespace. Vertex <c>v</c>'s color <c>c</c> is variable index <c>v * K + c</c>
    /// (vertex-major, color-minor: all of vertex 0's colors decided before vertex 1's), decided in
    /// ascending order; branch <c>1</c> means "<c>v</c> has color <c>c</c>". A coloring corresponds to the
    /// set of exactly one <c>(v, c)</c> pair per vertex, so the family this spec builds is not literally
    /// "a set of colorings" — it is a set of <c>(v, c)</c> pair-sets, each of which encodes one coloring.
    /// </para>
    /// <para>
    /// <b>State</b>: one slot per frontier vertex (from <see cref="VertexFrontierManager"/>, which supplies
    /// the introduce/forget bookkeeping even though it was written for one-variable-per-vertex specs — the
    /// frontier's vertex topology, and hence which vertex needs a slot when, does not depend on how many
    /// variables a vertex's own decision takes). A vertex's slot holds its chosen color once decided, or
    /// the sentinel <see cref="SlotEmpty"/> while still deciding among its <c>K</c> color variables (which
    /// also serves as "no color chosen yet" — since <c>0</c> is itself a valid color, zero-initialization
    /// cannot be reused as that sentinel the way most other specs in this namespace use it). One further
    /// slot, past the per-vertex ones, tracks <see cref="RepresentativesOnly"/>'s bookkeeping — see below.
    /// </para>
    /// <para>
    /// <b>Per vertex</b> <c>v</c>, across its <c>K</c> color variables: entering <c>v</c>'s first variable
    /// (color <c>0</c>) resets its slot to <see cref="SlotEmpty"/> (the "introduce" step). Taking color
    /// <c>c</c> is rejected outright if the slot is not still <see cref="SlotEmpty"/> (a color was already
    /// picked for <c>v</c> — at most one may be) or if an already-decided lower-indexed neighbor already
    /// has color <c>c</c>; otherwise the slot becomes <c>c</c>. After <c>v</c>'s last color variable
    /// (<c>c == K - 1</c>), the slot must no longer be <see cref="SlotEmpty"/> (a vertex needs at least one
    /// color) or the branch is rejected; the slots of any vertex <c>v</c> retires are then reset to
    /// <see cref="SlotEmpty"/> so a slot a later vertex reuses never inherits a stale color.
    /// </para>
    /// <para>
    /// <b><see cref="RepresentativesOnly"/></b>: relabeling a coloring's <c>k!</c> color permutations always
    /// produces another valid coloring, so every <i>class</i> of colorings-up-to-relabeling is counted
    /// <c>k!</c> times over (fewer, if not every color is used — see below). This option keeps exactly one
    /// representative per class: the one where, scanning vertices in index order, colors first appear in
    /// ascending order (<c>0</c>, then <c>1</c>, ...). One extra scalar slot (the array's last one) tracks
    /// the smallest color not yet introduced by any earlier vertex, starting at <c>0</c>; taking color
    /// <c>c &gt; </c> that value is rejected (it would introduce a color out of order), and taking exactly
    /// that value advances it. This is not a per-vertex frontier slot — it is global, read and written by
    /// every vertex's decision — so it does not affect <see cref="VertexFrontierManager.MaxFrontierSize"/>.
    /// For a coloring whose class uses exactly <c>m &lt;= k</c> colors, there are <c>k! / (k - m)!</c> ways
    /// to relabel it with colors from <c>0 .. k - 1</c>, of which this keeps exactly one: when every
    /// accepted coloring necessarily uses all <c>k</c> colors (e.g. <see cref="Graph.Complete"/> with
    /// <c>k</c> equal to its chromatic number), the representative count is exactly <c>1 / k!</c> of the
    /// full count; otherwise it is <c>(k - m)! / k!</c> of it, per class of size <c>m</c>.
    /// </para>
    /// </remarks>
    public readonly struct ColoringSpec : IArrayDdSpec
    {
        /// <summary>
        /// The sentinel marking a vertex's slot as "no color decided yet" — distinct from every real color
        /// (<c>0 .. K - 1</c>), including <c>0</c>.
        /// </summary>
        public const int SlotEmpty = -1;

        private readonly Graph _graph;
        private readonly int _k;
        private readonly bool _representativesOnly;
        private readonly VertexFrontierManager _frontierManager;
        private readonly int _variableCount;

        /// <summary>Creates a spec for proper <paramref name="k"/>-colorings of <paramref name="graph"/>.</summary>
        /// <param name="graph">The graph to color.</param>
        /// <param name="k">The number of available colors; must be positive.</param>
        /// <param name="representativesOnly">
        /// When <see langword="true"/>, keeps only the one representative coloring of each class equivalent
        /// under color relabeling — see the type remarks. Defaults to <see langword="false"/> (every proper
        /// coloring), which is what matches the chromatic polynomial.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="k"/> is not positive.</exception>
        public ColoringSpec(Graph graph, int k, bool representativesOnly = false)
        {
            ArgumentNullException.ThrowIfNull(graph);

            if (k <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(k), k, "Must be positive.");
            }

            _graph = graph;
            _k = k;
            _representativesOnly = representativesOnly;
            _frontierManager = new VertexFrontierManager(graph);
            _variableCount = checked(graph.VertexCount * k);
        }

        /// <summary>The graph this spec colors.</summary>
        public Graph Graph => _graph;

        /// <summary>The number of available colors.</summary>
        public int K => _k;

        /// <summary>Whether this spec keeps only one representative coloring per color-relabeling class.</summary>
        public bool RepresentativesOnly => _representativesOnly;

        /// <summary>The number of <c>(vertex, color)</c> variables: <c>Graph.VertexCount * K</c>.</summary>
        public int VariableCount => _variableCount;

        /// <summary>
        /// The slot tracking the smallest not-yet-introduced color, used only when
        /// <see cref="RepresentativesOnly"/> is set; one past the last per-vertex slot.
        /// </summary>
        private int NextNewColorSlot => _frontierManager.MaxFrontierSize;

        /// <inheritdoc/>
        public int ArrayLength => _frontierManager.MaxFrontierSize + 1;

        /// <inheritdoc/>
        public int GetRoot(Span<int> state)
        {
            // state is zero-filled by the caller: the NextNewColorSlot already reads 0, the correct start
            // for RepresentativesOnly's "smallest not-yet-introduced color". Vertex 0's slot needs an
            // explicit reset, though: 0 is a valid color, so the zero-fill cannot double as SlotEmpty.
            state[_frontierManager.Slot(0)] = SlotEmpty;
            return _variableCount;
        }

        /// <inheritdoc/>
        public int GetChild(Span<int> state, int level, int value)
        {
            int item = _variableCount - level;
            int v = item / _k;
            int c = item % _k;
            int slot = _frontierManager.Slot(v);

            if (c == 0 && v > 0)
            {
                state[slot] = SlotEmpty; // introduce: v's first color variable
            }

            if (value == 1)
            {
                if (state[slot] != SlotEmpty)
                {
                    return DdResult.False; // v already has a color; at most one is allowed
                }

                if (_representativesOnly && c > state[NextNewColorSlot])
                {
                    return DdResult.False; // would introduce color c before every color below it has been used
                }

                IReadOnlyList<int> earlierNeighborSlots = _frontierManager.EarlierNeighborSlots(v);
                for (int i = 0; i < earlierNeighborSlots.Count; i++)
                {
                    if (state[earlierNeighborSlots[i]] == c)
                    {
                        return DdResult.False; // an already-colored neighbor has this same color
                    }
                }

                state[slot] = c;

                if (_representativesOnly && c == state[NextNewColorSlot])
                {
                    state[NextNewColorSlot] = c + 1;
                }
            }

            if (c == _k - 1)
            {
                if (state[slot] == SlotEmpty)
                {
                    return DdResult.False; // v ends up with no color at all
                }

                IReadOnlyList<int> forgottenSlots = _frontierManager.ForgottenSlots(v);
                for (int i = 0; i < forgottenSlots.Count; i++)
                {
                    state[forgottenSlots[i]] = SlotEmpty; // clear so a reused slot never carries a stale color
                }
            }

            int remaining = level - 1;
            return remaining > 0 ? remaining : DdResult.True;
        }
    }
}

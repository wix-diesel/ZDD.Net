using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// <see cref="SpanningComponentState"/>'s comp-array mechanics, extended with two extra per-representative
    /// bits that <see cref="ArborescenceSpec"/>'s non-spanning mode needs: whether the component currently
    /// contains the fixed <c>root</c> vertex, and whether it has ever gained an arc (as opposed to still
    /// being the untouched singleton a vertex starts as). Both bits live at the same slot index as the comp
    /// code's representative, and are kept valid there — and only there — through every merge and forget, the
    /// same way <see cref="SpanningComponentState"/> only ever needs the representative's own comp slot to be
    /// authoritative for the whole component.
    /// </summary>
    /// <remarks>
    /// A component that never gained an arc (<c>hasEdge</c> still 0 when it closes) is just an untouched
    /// vertex — a trivial one-vertex "tree" that <see cref="ArborescenceSpec"/>'s non-spanning mode always
    /// allows to close, root or not. A component that did gain an arc is a real tree, and closing without
    /// <c>root</c> in it would mean a second, disconnected arborescence — not a single tree rooted at
    /// <c>root</c> — so <see cref="ArborescenceSpec"/> rejects that case using the two bits together.
    /// </remarks>
    internal static class ArborescenceComponentState
    {
        /// <summary>The slot is not currently occupied by a frontier vertex.</summary>
        internal const int SlotEmpty = SpanningComponentState.SlotEmpty;

        /// <summary>A newly introduced vertex starts as its own singleton component, untouched by any arc.</summary>
        internal static void Introduce(Span<int> comp, Span<int> hasRoot, Span<int> hasEdge, int slot, bool isRoot)
        {
            SpanningComponentState.Introduce(comp, slot);
            hasRoot[slot] = isRoot ? 1 : 0;
            hasEdge[slot] = 0;
        }

        /// <summary>
        /// Joins the components of the two vertices occupying <paramref name="su"/> and <paramref name="sv"/>,
        /// carrying both bits to the surviving representative and canonically zeroing them everywhere else —
        /// mirroring <see cref="SpanningComponentState.TryMerge"/>'s own loop rather than calling it, so both
        /// bits can be kept meaningful only at the representative slot in the same pass (see the class
        /// remarks: reading either bit anywhere but the current representative is never done, so every other
        /// slot is free to read as a fixed 0, keeping two histories that reach the same partition and the
        /// same root/edge facts comparable as equal states).
        /// </summary>
        /// <returns><see langword="false"/> if the two vertices were already in the same component — taking
        /// this arc would close a cycle.</returns>
        internal static bool TryMerge(Span<int> comp, Span<int> hasRoot, Span<int> hasEdge, int frontierLength, int su, int sv)
        {
            int repU = comp[su] - 1;
            int repV = comp[sv] - 1;

            if (repU == repV)
            {
                return false; // same component already: this arc would close a cycle
            }

            int keep = Math.Min(repU, repV);
            int drop = Math.Max(repU, repV);
            int keepCode = keep + 1;
            int mergedHasRoot = (hasRoot[repU] | hasRoot[repV]) != 0 ? 1 : 0;

            for (int slot = 0; slot < frontierLength; slot++)
            {
                if (comp[slot] != SlotEmpty && comp[slot] - 1 == drop)
                {
                    comp[slot] = keepCode;
                    hasRoot[slot] = 0;
                    hasEdge[slot] = 0;
                }
            }

            hasRoot[keep] = mergedHasRoot;
            hasEdge[keep] = 1; // merging always means at least one arc joined the two groups
            return true;
        }

        /// <summary>
        /// Retires <paramref name="slot"/>, whose vertex has just left the frontier, carrying both bits to
        /// the new representative if <see cref="SpanningComponentState.Forget"/> reassigns one.
        /// </summary>
        /// <param name="comp">The comp-array state.</param>
        /// <param name="hasRoot">The <c>hasRoot</c> bit array, indexed the same way as <paramref name="comp"/>.</param>
        /// <param name="hasEdge">The <c>hasEdge</c> bit array, indexed the same way as <paramref name="comp"/>.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="slot">The comp slot of the vertex leaving the frontier.</param>
        /// <param name="closingHasRoot">Whether the component that just closed (if it did) contained <c>root</c>.</param>
        /// <param name="closingHasEdge">Whether the component that just closed (if it did) ever gained an arc.</param>
        /// <returns><see langword="true"/> if no other frontier vertex belongs to this component anymore.</returns>
        internal static bool Forget(
            Span<int> comp, Span<int> hasRoot, Span<int> hasEdge, int frontierLength, int slot,
            out bool closingHasRoot, out bool closingHasEdge)
        {
            int rep = comp[slot] - 1;
            closingHasRoot = hasRoot[rep] != 0;
            closingHasEdge = hasEdge[rep] != 0;

            // The new representative, if Forget reassigns one, is always the smallest other member's slot —
            // find it the same way SpanningComponentState.Forget does internally, so the bits can follow it.
            int smallestOtherMember = int.MaxValue;
            for (int j = 0; j < frontierLength; j++)
            {
                if (j != slot && comp[j] != SpanningComponentState.SlotEmpty && comp[j] - 1 == rep && j < smallestOtherMember)
                {
                    smallestOtherMember = j;
                }
            }

            bool closed = SpanningComponentState.Forget(comp, frontierLength, slot);

            if (!closed && rep == slot)
            {
                hasRoot[smallestOtherMember] = closingHasRoot ? 1 : 0;
                hasEdge[smallestOtherMember] = closingHasEdge ? 1 : 0;
            }

            hasRoot[slot] = 0;
            hasEdge[slot] = 0;
            return closed;
        }
    }
}

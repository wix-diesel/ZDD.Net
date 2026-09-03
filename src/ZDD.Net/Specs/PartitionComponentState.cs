using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The comp-array mechanics <see cref="GraphPartitionSpec"/> uses. Like <see cref="ConnectedComponentState"/>,
    /// each frontier vertex's comp slot holds either <see cref="SlotEmpty"/> or a canonical representative
    /// code (<c>representativeSlot + 1</c>) — the frontier slot with the smallest index among the
    /// component's current members. A second, parallel array of the same length carries one running vertex
    /// count per component, kept correct only at whichever slot currently holds the representative; every
    /// other slot's count is stale and is never read. Merging two components is never rejected — a cycle
    /// among "kept" edges is a perfectly fine partition block, just as it is for <see cref="ConnectedSubgraphSpec"/>.
    /// </summary>
    internal static class PartitionComponentState
    {
        /// <summary>The slot is not currently occupied by a frontier vertex.</summary>
        internal const int SlotEmpty = 0;

        /// <summary>A newly introduced vertex starts as its own singleton, size-1 component.</summary>
        /// <param name="state">The comp-array state; the size array sits at <paramref name="frontierLength"/> and beyond.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="slot">The comp slot the newly introduced vertex occupies.</param>
        internal static void Introduce(Span<int> state, int frontierLength, int slot)
        {
            state[slot] = slot + 1;
            state[frontierLength + slot] = 1;
        }

        /// <summary>
        /// Joins the components of the two vertices occupying <paramref name="su"/> and <paramref name="sv"/>,
        /// summing their vertex counts. A no-op if they already share a component — taking this edge then
        /// just closes a cycle, which a partition block allows.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots.</param>
        /// <param name="su">The comp slot of one edge endpoint.</param>
        /// <param name="sv">The comp slot of the other edge endpoint.</param>
        internal static void Merge(Span<int> state, int frontierLength, int su, int sv)
        {
            int repU = state[su] - 1;
            int repV = state[sv] - 1;

            if (repU == repV)
            {
                return; // same component already: this edge just closes a cycle, nothing to merge
            }

            int keep = Math.Min(repU, repV);
            int drop = Math.Max(repU, repV);
            int mergedSize = state[frontierLength + repU] + state[frontierLength + repV];
            int keepCode = keep + 1;

            for (int slot = 0; slot < frontierLength; slot++)
            {
                if (state[slot] != SlotEmpty && state[slot] - 1 == drop)
                {
                    state[slot] = keepCode;
                }
            }

            state[frontierLength + keep] = mergedSize;
        }

        /// <summary>
        /// Retires <paramref name="slot"/>, whose vertex has just left the frontier. Mirrors
        /// <see cref="SpanningComponentState.Forget"/>: if it was the component's representative and other
        /// members remain, the representative — and its running vertex count — moves to the smallest
        /// remaining member's slot.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots.</param>
        /// <param name="slot">The comp slot of the vertex leaving the frontier.</param>
        /// <param name="closedSize">
        /// The component's final vertex count if it just closed (see the return value); otherwise <c>0</c>.
        /// </param>
        /// <returns><see langword="true"/> if no other frontier vertex belongs to this component anymore —
        /// the component has closed with a fixed, final size.</returns>
        internal static bool Forget(Span<int> state, int frontierLength, int slot, out int closedSize)
        {
            int rep = state[slot] - 1;
            bool hasOtherMember = false;
            int smallestOtherMember = int.MaxValue;

            for (int j = 0; j < frontierLength; j++)
            {
                if (j == slot || state[j] == SlotEmpty || state[j] - 1 != rep)
                {
                    continue;
                }

                hasOtherMember = true;
                if (j < smallestOtherMember)
                {
                    smallestOtherMember = j;
                }
            }

            if (hasOtherMember && rep == slot)
            {
                int newRepCode = smallestOtherMember + 1;
                state[frontierLength + smallestOtherMember] = state[frontierLength + slot];

                for (int j = 0; j < frontierLength; j++)
                {
                    if (j != slot && state[j] != SlotEmpty && state[j] - 1 == rep)
                    {
                        state[j] = newRepCode;
                    }
                }
            }

            closedSize = hasOtherMember ? 0 : state[frontierLength + slot];
            state[slot] = SlotEmpty;
            state[frontierLength + slot] = 0; // clear so a reused slot never inherits a stale size
            return !hasOtherMember;
        }
    }
}

using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The comp-array mechanics <see cref="VertexGroupSpec"/> uses. Like <see cref="PartitionComponentState"/>,
    /// each frontier vertex's comp slot holds either <see cref="SlotEmpty"/> or a canonical representative
    /// code (<c>representativeSlot + 1</c>). A second, parallel array of the same length carries which
    /// vertex group the component currently belongs to, kept correct only at whichever slot currently holds
    /// the representative; every other slot's entry is stale and is never read. <c>0</c> means the component
    /// so far holds only vertices with no group of their own ("free" vertices, per Graphillion's
    /// <c>vertex_groups</c> semantics — see <see cref="VertexGroupSpec"/>'s remarks); a positive value is the
    /// one-based id of the single group every grouped member of the component currently agrees on.
    /// </summary>
    internal static class VertexGroupComponentState
    {
        /// <summary>The slot is not currently occupied by a frontier vertex.</summary>
        internal const int SlotEmpty = 0;

        /// <summary>A newly introduced vertex starts as its own singleton component, bound to its own group.</summary>
        /// <param name="state">The comp-array state; the group array sits at <paramref name="frontierLength"/> and beyond.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="slot">The comp slot the newly introduced vertex occupies.</param>
        /// <param name="group">The vertex's one-based group id, or <c>0</c> if it belongs to no group.</param>
        internal static void Introduce(Span<int> state, int frontierLength, int slot, int group)
        {
            state[slot] = slot + 1;
            state[frontierLength + slot] = group;
        }

        /// <summary>
        /// Joins the components of the two vertices occupying <paramref name="su"/> and <paramref name="sv"/>,
        /// unless both are already bound to two different groups — merging them would put two vertex groups
        /// in one component, which <see cref="VertexGroupSpec"/> never allows. A no-op (and never a
        /// conflict) if they already share a component: taking this edge then just closes a cycle, which a
        /// vertex-group family allows freely.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots.</param>
        /// <param name="su">The comp slot of one edge endpoint.</param>
        /// <param name="sv">The comp slot of the other edge endpoint.</param>
        /// <param name="joinedSameGroup">
        /// <see langword="true"/> if the merge joined two previously distinct components that were <b>both</b>
        /// already bound to the same group — the caller's per-group open-component counter must drop by one.
        /// </param>
        /// <returns>
        /// <see langword="false"/> if the two vertices' components are already each bound to a different,
        /// non-zero group; the merge is rejected and <paramref name="state"/> is left untouched.
        /// </returns>
        internal static bool Merge(Span<int> state, int frontierLength, int su, int sv, out bool joinedSameGroup)
        {
            joinedSameGroup = false;

            int repU = state[su] - 1;
            int repV = state[sv] - 1;

            if (repU == repV)
            {
                return true; // same component already: this edge just closes a cycle, nothing to merge
            }

            int groupU = state[frontierLength + repU];
            int groupV = state[frontierLength + repV];

            if (groupU != 0 && groupV != 0)
            {
                if (groupU != groupV)
                {
                    return false; // two different vertex groups would end up sharing a component
                }

                joinedSameGroup = true;
            }

            int mergedGroup = groupU != 0 ? groupU : groupV;
            int keep = Math.Min(repU, repV);
            int drop = Math.Max(repU, repV);
            int keepCode = keep + 1;

            for (int slot = 0; slot < frontierLength; slot++)
            {
                if (state[slot] != SlotEmpty && state[slot] - 1 == drop)
                {
                    state[slot] = keepCode;
                }
            }

            state[frontierLength + keep] = mergedGroup;
            return true;
        }

        /// <summary>
        /// Retires <paramref name="slot"/>, whose vertex has just left the frontier. Mirrors
        /// <see cref="PartitionComponentState.Forget"/>: if it was the component's representative and other
        /// members remain, the representative — and its bound group — moves to the smallest remaining
        /// member's slot.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots.</param>
        /// <param name="slot">The comp slot of the vertex leaving the frontier.</param>
        /// <param name="closedGroup">
        /// The component's bound group (<c>0</c> if it never gained a grouped member) if it just closed (see
        /// the return value); otherwise <c>0</c>.
        /// </param>
        /// <returns><see langword="true"/> if no other frontier vertex belongs to this component anymore —
        /// the component has closed and can never gain another member.</returns>
        internal static bool Forget(Span<int> state, int frontierLength, int slot, out int closedGroup)
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

            closedGroup = hasOtherMember ? 0 : state[frontierLength + slot];
            state[slot] = SlotEmpty;
            state[frontierLength + slot] = 0; // clear so a reused slot never inherits a stale group
            return !hasOtherMember;
        }
    }
}

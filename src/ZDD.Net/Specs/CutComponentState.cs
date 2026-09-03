using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The comp-array mechanics <see cref="CutSpec"/> uses. Like <see cref="ConnectedComponentState"/>, each
    /// frontier vertex's comp slot holds either <see cref="SlotEmpty"/> or a canonical representative code
    /// (<c>representativeSlot + 1</c>). A second, parallel array of the same length carries a per-component
    /// side flag — <see cref="FlagNone"/>, <see cref="FlagS"/>, or <see cref="FlagT"/> — kept correct only at
    /// whichever slot currently holds the representative, exactly as <see cref="PartitionComponentState"/>
    /// keeps its size counter. Unlike that spec, <see cref="Merge"/> can fail: joining an <see cref="FlagS"/>
    /// component with an <see cref="FlagT"/> one would reconnect <c>s</c> and <c>t</c>, which a cut can
    /// never allow.
    /// </summary>
    internal static class CutComponentState
    {
        /// <summary>The slot is not currently occupied by a frontier vertex.</summary>
        internal const int SlotEmpty = 0;

        /// <summary>The component holds neither <c>s</c> nor <c>t</c> (so far).</summary>
        internal const int FlagNone = 0;

        /// <summary>The component holds <c>s</c>.</summary>
        internal const int FlagS = 1;

        /// <summary>The component holds <c>t</c>.</summary>
        internal const int FlagT = 2;

        /// <summary>A newly introduced vertex starts as its own singleton component, carrying <paramref name="flag"/>.</summary>
        /// <param name="state">The comp-array state; the flag array sits at <paramref name="frontierLength"/> and beyond.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="slot">The comp slot the newly introduced vertex occupies.</param>
        /// <param name="flag"><see cref="FlagS"/> for <c>s</c>, <see cref="FlagT"/> for <c>t</c>, otherwise <see cref="FlagNone"/>.</param>
        internal static void Introduce(Span<int> state, int frontierLength, int slot, int flag)
        {
            state[slot] = slot + 1;
            state[frontierLength + slot] = flag;
        }

        /// <summary>
        /// Joins the components of the two vertices occupying <paramref name="su"/> and <paramref name="sv"/>.
        /// A no-op (and always succeeds) if they already share a component — this edge then just closes a
        /// cycle within one side, which is fine.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots.</param>
        /// <param name="su">The comp slot of one edge endpoint.</param>
        /// <param name="sv">The comp slot of the other edge endpoint.</param>
        /// <returns>
        /// <see langword="false"/> if the merge would join an <see cref="FlagS"/> component with an
        /// <see cref="FlagT"/> one — <c>s</c> and <c>t</c> would end up on the same side, so keeping this
        /// edge is invalid.
        /// </returns>
        internal static bool Merge(Span<int> state, int frontierLength, int su, int sv)
        {
            int repU = state[su] - 1;
            int repV = state[sv] - 1;

            if (repU == repV)
            {
                return true; // same component already: this edge just closes a cycle, nothing to merge
            }

            int flagU = state[frontierLength + repU];
            int flagV = state[frontierLength + repV];

            if (flagU != FlagNone && flagV != FlagNone && flagU != flagV)
            {
                return false; // would join the s-side and the t-side into one component
            }

            int mergedFlag = flagU != FlagNone ? flagU : flagV;
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

            state[frontierLength + keep] = mergedFlag;
            return true;
        }

        /// <summary>
        /// Retires <paramref name="slot"/>, whose vertex has just left the frontier. Mirrors
        /// <see cref="PartitionComponentState.Forget"/>: if it was the component's representative and other
        /// members remain, the representative — and its flag — moves to the smallest remaining member's slot.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots.</param>
        /// <param name="slot">The comp slot of the vertex leaving the frontier.</param>
        /// <param name="flag">The component's current flag, whether or not it just closed.</param>
        /// <returns><see langword="true"/> if no other frontier vertex belongs to this component anymore —
        /// the component has closed.</returns>
        internal static bool Forget(Span<int> state, int frontierLength, int slot, out int flag)
        {
            int rep = state[slot] - 1;
            flag = state[frontierLength + rep];
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
                state[frontierLength + smallestOtherMember] = flag;

                for (int j = 0; j < frontierLength; j++)
                {
                    if (j != slot && state[j] != SlotEmpty && state[j] - 1 == rep)
                    {
                        state[j] = newRepCode;
                    }
                }
            }

            state[slot] = SlotEmpty;
            state[frontierLength + slot] = FlagNone; // clear so a reused slot never inherits a stale flag
            return !hasOtherMember;
        }
    }
}

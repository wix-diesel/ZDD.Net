using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The comp-array mechanics shared by <see cref="SpanningTreeSpec"/> and <see cref="ForestSpec"/>: each
    /// frontier vertex's state-array slot holds either <see cref="SlotEmpty"/> (not currently in the
    /// frontier) or <c>representativeSlot + 1</c>, where the representative is <b>the frontier slot with
    /// the smallest index among all vertices currently in the same connected component</b>. That choice of
    /// representative — rather than, say, whichever slot happened to be visited first — is the
    /// canonicalization the comp array needs: two states that describe the same partition of the frontier
    /// into components always carry identical arrays, regardless of the order edges were taken in to reach
    /// them, so the builder's node table can actually recognize them as equal and merge the branches.
    /// </summary>
    internal static class SpanningComponentState
    {
        /// <summary>The slot is not currently occupied by a frontier vertex.</summary>
        internal const int SlotEmpty = 0;

        /// <summary>A newly introduced vertex starts as its own singleton component.</summary>
        internal static void Introduce(Span<int> state, int slot)
        {
            state[slot] = slot + 1;
        }

        /// <summary>
        /// Joins the components of the two vertices occupying <paramref name="su"/> and <paramref name="sv"/>.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="su">The comp slot of one edge endpoint.</param>
        /// <param name="sv">The comp slot of the other edge endpoint.</param>
        /// <returns><see langword="false"/> if the two vertices were already in the same component — taking
        /// this edge would close a cycle.</returns>
        internal static bool TryMerge(Span<int> state, int frontierLength, int su, int sv)
        {
            int repU = state[su] - 1;
            int repV = state[sv] - 1;

            if (repU == repV)
            {
                return false; // same component already: this edge would close a cycle
            }

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

            return true;
        }

        /// <summary>
        /// Retires <paramref name="slot"/>, whose vertex has just left the frontier. If it was the
        /// component's representative and other members remain, the representative moves to the smallest
        /// remaining member's slot (preserving the "smallest slot" canonicalization); the slot itself is
        /// always cleared to <see cref="SlotEmpty"/>, since a stale leftover code would keep an otherwise
        /// identical future state from merging with this one.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="slot">The comp slot of the vertex leaving the frontier.</param>
        /// <returns><see langword="true"/> if no other frontier vertex belongs to this component anymore —
        /// the component has closed and can never gain another member.</returns>
        internal static bool Forget(Span<int> state, int frontierLength, int slot)
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
                for (int j = 0; j < frontierLength; j++)
                {
                    if (j != slot && state[j] != SlotEmpty && state[j] - 1 == rep)
                    {
                        state[j] = newRepCode;
                    }
                }
            }

            state[slot] = SlotEmpty;
            return !hasOtherMember;
        }
    }
}

using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The comp-array mechanics <see cref="ComponentCountSpec"/> uses. Like <see cref="ConnectedComponentState"/>,
    /// each frontier vertex's slot holds either <see cref="SlotEmpty"/> or a canonical representative code —
    /// the frontier slot with the smallest index among the component's current members — with the sign
    /// carrying one extra bit of information for free: a <b>positive</b> code marks a component that has
    /// never had an edge taken within it (still a bare singleton), a <b>negative</b> code marks one that has
    /// (so it spans at least two vertices). <see cref="ComponentCountSpec"/> only counts the latter kind when
    /// a component closes — see its remarks for why. Merging two components already in the same one (a
    /// cycle) is never rejected, exactly as <see cref="ConnectedComponentState"/> and
    /// <see cref="PartitionComponentState"/> allow.
    /// </summary>
    internal static class ComponentCountComponentState
    {
        /// <summary>The slot is not currently occupied by a frontier vertex.</summary>
        internal const int SlotEmpty = 0;

        /// <summary>A newly introduced vertex starts as its own singleton component, with no edge yet.</summary>
        internal static void Introduce(Span<int> state, int slot)
        {
            state[slot] = slot + 1;
        }

        /// <summary>
        /// Joins the components of the two vertices occupying <paramref name="su"/> and <paramref name="sv"/>,
        /// marking the result as having an edge — taking this edge means the merged component (or, if the two
        /// were already the same component, the cycle it just closed) is no longer a bare singleton.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="su">The comp slot of one edge endpoint.</param>
        /// <param name="sv">The comp slot of the other edge endpoint.</param>
        internal static void Merge(Span<int> state, int frontierLength, int su, int sv)
        {
            int codeU = state[su];
            int codeV = state[sv];
            int repU = (codeU < 0 ? -codeU : codeU) - 1;
            int repV = (codeV < 0 ? -codeV : codeV) - 1;

            int keepRep = Math.Min(repU, repV);
            int dropRep = Math.Max(repU, repV);
            int keepCode = -(keepRep + 1);

            for (int slot = 0; slot < frontierLength; slot++)
            {
                int code = state[slot];
                if (code == SlotEmpty)
                {
                    continue;
                }

                int rep = (code < 0 ? -code : code) - 1;
                if (rep == keepRep || rep == dropRep)
                {
                    state[slot] = keepCode;
                }
            }
        }

        /// <summary>
        /// Retires <paramref name="slot"/>, whose vertex has just left the frontier. Mirrors
        /// <see cref="SpanningComponentState.Forget"/>: if it was the component's representative and other
        /// members remain, the representative moves to the smallest remaining member's slot, carrying the
        /// same sign along.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="slot">The comp slot of the vertex leaving the frontier.</param>
        /// <param name="hadEdge">Whether the closing component ever had an edge taken within it.</param>
        /// <returns><see langword="true"/> if no other frontier vertex belongs to this component anymore —
        /// the component has closed and can never gain another member.</returns>
        internal static bool Forget(Span<int> state, int frontierLength, int slot, out bool hadEdge)
        {
            int code = state[slot];
            int rep = (code < 0 ? -code : code) - 1;
            hadEdge = code < 0;

            bool hasOtherMember = false;
            int smallestOtherMember = int.MaxValue;

            for (int j = 0; j < frontierLength; j++)
            {
                if (j == slot || state[j] == SlotEmpty)
                {
                    continue;
                }

                int otherRep = (state[j] < 0 ? -state[j] : state[j]) - 1;
                if (otherRep != rep)
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
                int newRepCode = hadEdge ? -(smallestOtherMember + 1) : smallestOtherMember + 1;
                for (int j = 0; j < frontierLength; j++)
                {
                    if (j == slot || state[j] == SlotEmpty)
                    {
                        continue;
                    }

                    int otherRep = (state[j] < 0 ? -state[j] : state[j]) - 1;
                    if (otherRep == rep)
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

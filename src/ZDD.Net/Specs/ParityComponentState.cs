using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// A weighted ("parity") union-find <see cref="CutSpec"/>'s <c>MinimalOnly</c> mode uses to reject a
    /// redundant cut: a decision to cut edge <c>(u, v)</c> is only ever necessary if <c>u</c> and <c>v</c>
    /// never end up connected by kept edges. That is a <i>global</i> property — the connecting path, if any,
    /// can be completed by edges decided much later — so it cannot be read off <see cref="CutComponentState"/>
    /// (which only ever merges on <i>kept</i> edges) at decision time. Instead, every decided edge, kept or
    /// cut, is folded into <b>this</b> structure as a same/different constraint between its endpoints
    /// (kept &#8658; same, cut &#8658; different); a contradiction — the constraint conflicts with ones
    /// already implied — means the current branch's kept/cut pattern is not a consistent 2-coloring at all,
    /// and is rejected. <see cref="CutComponentState"/> still separately guarantees the two <i>sides</i> are
    /// each actually connected (a 2-coloring can be globally consistent yet still disconnected); the two
    /// checks together are exactly the connected-bipartition characterization of a minimal cut.
    /// </summary>
    /// <remarks>
    /// Encoding mirrors <see cref="ConnectedComponentState"/>: each slot holds either <see cref="SlotEmpty"/>
    /// or a signed representative code, <c>&#177;(representativeSlot + 1)</c> — here the sign is the slot's
    /// parity <i>relative to its representative</i> (positive = same, negative = different), not a terminal
    /// flag. Because merges always rewrite every affected slot outright (no lazy path compression), a
    /// representative's parity relative to itself is always positive by construction.
    /// </remarks>
    internal static class ParityComponentState
    {
        /// <summary>The slot is not currently occupied by a frontier vertex.</summary>
        internal const int SlotEmpty = 0;

        /// <summary>A newly introduced vertex starts as its own singleton, at parity 0 relative to itself.</summary>
        internal static void Introduce(Span<int> state, int slot)
        {
            state[slot] = slot + 1;
        }

        /// <summary>
        /// Records that the vertices occupying <paramref name="su"/> and <paramref name="sv"/> must end up
        /// the same color (a kept edge) or different colors (a cut edge).
        /// </summary>
        /// <param name="state">The parity-array state.</param>
        /// <param name="frontierLength">The number of parity slots.</param>
        /// <param name="su">The parity slot of one edge endpoint.</param>
        /// <param name="sv">The parity slot of the other edge endpoint.</param>
        /// <param name="mustBeSame"><see langword="true"/> for a kept edge, <see langword="false"/> for a cut edge.</param>
        /// <returns>
        /// <see langword="false"/> if this contradicts a constraint already implied by earlier decisions —
        /// the edges decided so far, together with this one, admit no consistent 2-coloring at all.
        /// </returns>
        internal static bool Union(Span<int> state, int frontierLength, int su, int sv, bool mustBeSame)
        {
            int codeU = state[su];
            int codeV = state[sv];
            int repU = (codeU < 0 ? -codeU : codeU) - 1;
            int repV = (codeV < 0 ? -codeV : codeV) - 1;
            int parityU = codeU < 0 ? 1 : 0;
            int parityV = codeV < 0 ? 1 : 0;
            int desiredRelativeParity = mustBeSame ? 0 : 1;

            if (repU == repV)
            {
                return (parityU ^ parityV) == desiredRelativeParity;
            }

            // Fold whichever group has the larger representative into the smaller one (same canonicalization
            // as every other comp array here), flipping its members' parity just enough that u, v end up at
            // exactly the desired relative parity under the surviving representative.
            int flip = parityU ^ parityV ^ desiredRelativeParity;
            int keep = Math.Min(repU, repV);
            int drop = Math.Max(repU, repV);

            for (int slot = 0; slot < frontierLength; slot++)
            {
                int code = state[slot];
                if (code == SlotEmpty)
                {
                    continue;
                }

                int rep = (code < 0 ? -code : code) - 1;
                if (rep != drop)
                {
                    continue;
                }

                int newParity = (code < 0 ? 1 : 0) ^ flip;
                state[slot] = newParity == 1 ? -(keep + 1) : keep + 1;
            }

            return true;
        }

        /// <summary>
        /// Retires <paramref name="slot"/>, whose vertex has just left the frontier. Unlike
        /// <see cref="CutComponentState.Forget"/>, nothing here is ever rejected — this structure only ever
        /// answers "is this pattern of decisions self-consistent", not "is it complete" — so this purely
        /// maintains the representative-and-parity bookkeeping.
        /// </summary>
        /// <param name="state">The parity-array state.</param>
        /// <param name="frontierLength">The number of parity slots.</param>
        /// <param name="slot">The parity slot of the vertex leaving the frontier.</param>
        internal static void Forget(Span<int> state, int frontierLength, int slot)
        {
            int code = state[slot];
            int rep = (code < 0 ? -code : code) - 1;

            if (rep == slot)
            {
                int smallestOtherMember = int.MaxValue;
                for (int j = 0; j < frontierLength; j++)
                {
                    if (j == slot || state[j] == SlotEmpty || (state[j] < 0 ? -state[j] : state[j]) - 1 != rep)
                    {
                        continue;
                    }

                    if (j < smallestOtherMember)
                    {
                        smallestOtherMember = j;
                    }
                }

                if (smallestOtherMember != int.MaxValue)
                {
                    // Rebase every remaining member's parity onto the new representative: relative-to-new =
                    // relative-to-old XOR (new representative's own relative-to-old parity).
                    int newRepParity = state[smallestOtherMember] < 0 ? 1 : 0;
                    for (int j = 0; j < frontierLength; j++)
                    {
                        if (j == slot || state[j] == SlotEmpty)
                        {
                            continue;
                        }

                        int code2 = state[j];
                        if ((code2 < 0 ? -code2 : code2) - 1 != rep)
                        {
                            continue;
                        }

                        int rebased = (code2 < 0 ? 1 : 0) ^ newRepParity;
                        state[j] = rebased == 1 ? -(smallestOtherMember + 1) : smallestOtherMember + 1;
                    }
                }
            }

            state[slot] = SlotEmpty;
        }
    }
}

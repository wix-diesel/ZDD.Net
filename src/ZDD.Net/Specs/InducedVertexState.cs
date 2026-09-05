using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The three-value per-vertex mechanics <see cref="InducedSubgraphSpec"/> uses: every frontier vertex
    /// is <see cref="Unknown"/> (not yet touched by a taken edge), <see cref="In"/> (touched by one), or
    /// <see cref="Out"/> (confirmed to never be touched — either because taking an edge to it was rejected,
    /// or because a not-taken edge forced it). Shared as a standalone helper because M6-13's
    /// <c>BicliqueSpec</c> needs the identical three-value structure (<c>SideA</c> / <c>SideB</c> /
    /// <c>Unused</c>) with its own transition rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a vertex can't just flip to <see cref="Out"/> the moment a not-taken edge is decided</b>:
    /// taking an edge <c>(u,v)</c> requires both endpoints end up <see cref="In"/>; not taking it forbids
    /// both ending up <see cref="In"/>. When one endpoint is already <see cref="In"/> at that moment the
    /// other can be fixed <see cref="Out"/> right away (<see cref="MarkNotAdjacent"/>) — but when
    /// <i>both</i> are still <see cref="Unknown"/>, fixing either one now would be guessing: either could
    /// still legitimately become <see cref="In"/> later, just not both. Splitting into "which one becomes
    /// Out" sub-states here is exactly the branching <see cref="InducedSubgraphSpec"/>'s design note warns
    /// against, so both are simply left <see cref="Unknown"/> and the constraint is re-checked later,
    /// against whichever one is still around to check (<see cref="MarkIn"/>).
    /// </para>
    /// <para>
    /// <b>Why <see cref="MarkIn"/> still catches it</b>: a vertex can only move <see cref="Unknown"/> →
    /// <see cref="In"/> here, which means every earlier edge of its own was decided not-taken (a taken one
    /// would have already set it <see cref="In"/>). So when it does move, every graph-neighbor connected by
    /// an earlier, still-undecided-looking edge is exactly a not-taken edge whose fate depended on this
    /// vertex — and this is the last moment that neighbor's slot is guaranteed to still hold its own answer
    /// (a neighbor that already left the frontier <see cref="In"/> would have run this very check against
    /// <i>this</i> vertex back when it transitioned, since this vertex was necessarily still present then).
    /// So checking each such earlier, still-present neighbor here — rejecting if it is already
    /// <see cref="In"/>, else fixing it <see cref="Out"/> — is enough to catch the constraint no matter
    /// which of the two ends resolves first.
    /// </para>
    /// </remarks>
    internal static class InducedVertexState
    {
        /// <summary>Not yet touched by any taken edge; may still become <see cref="In"/> or <see cref="Out"/>.</summary>
        internal const int Unknown = 0;

        /// <summary>Touched by a taken edge; sticky until the vertex is forgotten and discarded.</summary>
        internal const int In = 1;

        /// <summary>Confirmed to never be touched by any taken edge.</summary>
        internal const int Out = 2;

        /// <summary>
        /// Moves the vertex at <paramref name="slot"/> from <see cref="Unknown"/> to <see cref="In"/>,
        /// enforcing the induced-subgraph constraint against every earlier-decided, still-present
        /// graph-neighbor in <paramref name="priorNeighborSlots"/> (see the type remarks for why this list
        /// is exactly the right set to check). Callers must have already rejected <see cref="Out"/> and
        /// must only call this when <paramref name="slot"/> currently reads <see cref="Unknown"/>.
        /// </summary>
        /// <param name="state">The state array.</param>
        /// <param name="slot">The frontier slot transitioning to <see cref="In"/>.</param>
        /// <param name="priorNeighborSlots">
        /// The slots of graph-neighbors connected by an earlier edge that are still in the frontier —
        /// precomputed once per edge/endpoint, since it depends only on graph structure and the edge order.
        /// </param>
        /// <returns>
        /// <see langword="false"/> if some entry in <paramref name="priorNeighborSlots"/> is already
        /// <see cref="In"/> — the not-taken edge between it and this vertex would need both endpoints
        /// <see cref="In"/>, which is exactly forbidden. On <see langword="true"/>, every <see cref="Unknown"/>
        /// entry has been fixed <see cref="Out"/> and <paramref name="slot"/> now reads <see cref="In"/>.
        /// </returns>
        internal static bool MarkIn(Span<int> state, int slot, int[] priorNeighborSlots)
        {
            for (int i = 0; i < priorNeighborSlots.Length; i++)
            {
                int otherSlot = priorNeighborSlots[i];

                if (state[otherSlot] == In)
                {
                    return false;
                }

                if (state[otherSlot] == Unknown)
                {
                    state[otherSlot] = Out;
                }
            }

            state[slot] = In;
            return true;
        }

        /// <summary>
        /// Records that the edge between <paramref name="slotA"/> and <paramref name="slotB"/> was not
        /// taken: if one side is already <see cref="In"/> and the other still <see cref="Unknown"/>, the
        /// other can never become <see cref="In"/> now, so it is fixed <see cref="Out"/> immediately —
        /// waiting would risk it being forgotten (and discarded) before that could be checked. If both are
        /// still <see cref="Unknown"/>, neither is touched (see the type remarks for why); the caller must
        /// separately reject the case where both are already <see cref="In"/>.
        /// </summary>
        internal static void MarkNotAdjacent(Span<int> state, int slotA, int slotB)
        {
            if (state[slotA] == In && state[slotB] == Unknown)
            {
                state[slotB] = Out;
            }
            else if (state[slotB] == In && state[slotA] == Unknown)
            {
                state[slotA] = Out;
            }
        }
    }
}

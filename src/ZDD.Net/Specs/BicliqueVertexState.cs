using System;
using System.Collections.Generic;
using ZDD.Net.Graphs;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The per-vertex/per-group mechanics <see cref="BicliqueSpec"/> uses to track which vertices end up on
    /// which of the biclique's two sides. Structurally the same three-outcome idea M6-12's
    /// <c>InducedVertexState</c> uses (a frontier vertex is untouched, on one side, or on the other, and the
    /// "untouched" code doubles as the eventual "unused" outcome), but the transition rules are genuinely
    /// different — see the remarks below for why a biclique needs a full parity union-find rather than
    /// <c>InducedVertexState</c>'s single prior-neighbor check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a single global "SideA" / "SideB" label does not work</b>: a biclique's edge set, once
    /// non-empty, is one connected piece, but the taken edges that eventually connect two far-apart parts of
    /// it can be decided in either order. Two edges that are each some vertex's very first taken edge (so
    /// each starts its own two-vertex group with an arbitrary "which one is which side" choice) may turn out
    /// later, once a connecting edge is taken between them, to need one of the two groups' labels flipped —
    /// there was no way to know in advance which arbitrary choice would end up compatible. A single fixed
    /// label picked the moment a vertex is first touched cannot be undone, so it is wrong roughly half the
    /// time whenever more than one group ever forms before they all merge. A parity union-find (mirroring
    /// <c>ParityComponentState</c>'s representative-and-relative-parity encoding) sidesteps this entirely:
    /// each vertex's side is only ever asked "same as the representative, or different", and merging two
    /// groups can retroactively flip an entire group's relative parity in one pass — the same trick
    /// <c>ParityComponentState</c> uses to fold a same/different requirement in after the fact.
    /// </para>
    /// <para>
    /// <b>Why a graph-adjacency check on just the two endpoints of the edge being merged is not enough</b>:
    /// a complete bipartite graph requires <i>every</i> cross pair between the two sides to be an actual,
    /// taken edge — including pairs that are not graph-adjacent at all (which can never satisfy that) and
    /// pairs that are adjacent but whose shared edge was already decided <i>not taken</i> while both were
    /// still untouched (deferred, per the spec). When two whole groups merge through one taken edge, this
    /// has to be checked for <i>every</i> pair that is newly on opposite sides because of the merge, not just
    /// the merging edge's own two endpoints — two other members, one from each group, might never share an
    /// edge with each other at all. <see cref="TryMerge"/> therefore scans every currently-present pair
    /// across the two groups once, up front.
    /// </para>
    /// <para>
    /// <b>What no live scan can see</b>: a vertex that has already left the frontier (forgotten). For that,
    /// each group tracks, on its representative, whether either of its two relative sides has ever had a
    /// member forgotten — once one has, the other side can never validly gain <i>another</i> member (that
    /// member could never be verified adjacent to the vertex that already left); <see cref="TryMerge"/>
    /// rejects a merge that would do that, using flags folded in from both sides' history (with whichever
    /// side flips, if either group's relative parity has to flip to align them).
    /// </para>
    /// </remarks>
    internal static class BicliqueVertexState
    {
        /// <summary>Not yet part of any group; may still join one, or end up unused.</summary>
        internal const int Free = 0;

        private const int RelativeSideAForgotten = 1;
        private const int RelativeSideBForgotten = 2;
        private const int RelativeSideAPresent = 4;
        private const int RelativeSideBPresent = 8;

        /// <summary>Whether <paramref name="slot"/> currently belongs to some group (has taken at least one edge).</summary>
        internal static bool IsGrouped(ReadOnlySpan<int> code, int slot) => code[slot] != Free;

        /// <summary>The representative slot of the group <paramref name="slot"/> belongs to.</summary>
        internal static int Representative(ReadOnlySpan<int> code, int slot)
        {
            int value = code[slot];
            return (value < 0 ? -value : value) - 1;
        }

        /// <summary>The side of <paramref name="slot"/>, <c>0</c> or <c>1</c>, relative to its group's representative.</summary>
        internal static int RelativeSide(ReadOnlySpan<int> code, int slot) => code[slot] < 0 ? 1 : 0;

        /// <summary>Turns <paramref name="slot"/> (currently <see cref="Free"/>) into its own new, single-member group.</summary>
        internal static void CreateSingleton(Span<int> code, Span<int> flags, Span<int> countSideA, Span<int> countSideB, int slot)
        {
            code[slot] = slot + 1;
            flags[slot] = RelativeSideAPresent; // a fresh singleton is relative side 0 ("A") to itself

            if (!countSideA.IsEmpty)
            {
                countSideA[slot] = 1;
                countSideB[slot] = 0;
            }
        }

        /// <summary>
        /// Requires the vertices at <paramref name="su"/> and <paramref name="sv"/> (both already grouped) to
        /// end up on opposite sides. If they are already in the same group, this is a plain parity check;
        /// otherwise their two groups are merged, after verifying every currently-present cross pair the
        /// merge newly puts on opposite sides is graph-adjacent by an edge not already decided (see the type
        /// remarks), and that neither side about to gain members has already had the opposite side lose one.
        /// </summary>
        /// <param name="code">The per-slot group/parity code array.</param>
        /// <param name="vertexOfSlot">Which graph vertex currently occupies each slot.</param>
        /// <param name="flags">Per-representative-slot forgotten-side flags.</param>
        /// <param name="countSideA">Per-representative-slot running count of relative-side-0 members; empty when not size-fixed.</param>
        /// <param name="countSideB">Per-representative-slot running count of relative-side-1 members; empty when not size-fixed.</param>
        /// <param name="frontierLength">The number of slots.</param>
        /// <param name="su">One edge endpoint's slot.</param>
        /// <param name="sv">The other edge endpoint's slot.</param>
        /// <param name="currentEdgeIndex">The edge index currently being decided.</param>
        /// <param name="edgeIndexOf">Every graph edge's index, keyed by its (order-independent) endpoints.</param>
        /// <param name="maxSideSize">The size-fixed overload's cap on either side's running count; ignored when not size-fixed.</param>
        /// <param name="groupsReduced">
        /// <see langword="true"/> if two previously-separate groups were merged into one (the caller should
        /// decrement its distinct-group counter); <see langword="false"/> if they were already the same group.
        /// </param>
        internal static bool TryMerge(
            Span<int> code,
            Span<int> vertexOfSlot,
            Span<int> flags,
            Span<int> countSideA,
            Span<int> countSideB,
            int frontierLength,
            int su,
            int sv,
            int currentEdgeIndex,
            Dictionary<Edge, int> edgeIndexOf,
            int maxSideSize,
            out bool groupsReduced)
        {
            groupsReduced = false;
            int repU = Representative(code, su);
            int repV = Representative(code, sv);
            int parityU = RelativeSide(code, su);
            int parityV = RelativeSide(code, sv);

            if (repU == repV)
            {
                return (parityU ^ parityV) == 1; // must already be on opposite sides; same side is a contradiction
            }

            bool sizeFixed = !countSideA.IsEmpty;
            int flip = parityU ^ parityV ^ 1;
            int keep = Math.Min(repU, repV);
            int drop = Math.Max(repU, repV);

            int dropFlags = flags[drop];
            bool dropSideAForgotten = flip == 0
                ? (dropFlags & RelativeSideAForgotten) != 0
                : (dropFlags & RelativeSideBForgotten) != 0;
            bool dropSideBForgotten = flip == 0
                ? (dropFlags & RelativeSideBForgotten) != 0
                : (dropFlags & RelativeSideAForgotten) != 0;
            bool dropHasSideA = flip == 0
                ? (dropFlags & RelativeSideAPresent) != 0
                : (dropFlags & RelativeSideBPresent) != 0;
            bool dropHasSideB = flip == 0
                ? (dropFlags & RelativeSideBPresent) != 0
                : (dropFlags & RelativeSideAPresent) != 0;

            int keepFlags = flags[keep];
            bool keepSideAForgotten = (keepFlags & RelativeSideAForgotten) != 0;
            bool keepSideBForgotten = (keepFlags & RelativeSideBForgotten) != 0;
            bool keepHasSideA = (keepFlags & RelativeSideAPresent) != 0;
            bool keepHasSideB = (keepFlags & RelativeSideBPresent) != 0;

            // Neither side about to gain members (from either group's contribution) may already have had
            // the opposite side lose a member — that departed vertex could never be verified adjacent to a
            // member arriving only now. Checked in both directions: keep's history against drop's arrivals,
            // and drop's history against keep's (newly related, from drop's perspective) existing members.
            if ((dropHasSideA && keepSideBForgotten) || (dropHasSideB && keepSideAForgotten))
            {
                return false;
            }

            if ((keepHasSideA && dropSideBForgotten) || (keepHasSideB && dropSideAForgotten))
            {
                return false;
            }

            // Every currently-present pair the merge newly puts on opposite sides must be graph-adjacent by
            // an edge that has not already been decided (an already-decided one, since neither endpoint was
            // grouped with the other before, must have been not-taken — exactly the violation to catch).
            for (int i = 0; i < frontierLength; i++)
            {
                if (code[i] == Free || Representative(code, i) != keep)
                {
                    continue;
                }

                int finalSideOfI = RelativeSide(code, i);

                for (int j = 0; j < frontierLength; j++)
                {
                    if (code[j] == Free || Representative(code, j) != drop)
                    {
                        continue;
                    }

                    int finalSideOfJ = RelativeSide(code, j) ^ flip;
                    if (finalSideOfI == finalSideOfJ)
                    {
                        continue; // ending up on the same side: no adjacency required
                    }

                    if (!edgeIndexOf.TryGetValue(new Edge(vertexOfSlot[i], vertexOfSlot[j]), out int otherEdgeIndex) ||
                        otherEdgeIndex < currentEdgeIndex)
                    {
                        return false;
                    }
                }
            }

            if (sizeFixed)
            {
                int dropSideA = flip == 0 ? countSideA[drop] : countSideB[drop];
                int dropSideB = flip == 0 ? countSideB[drop] : countSideA[drop];
                int mergedSideA = countSideA[keep] + dropSideA;
                int mergedSideB = countSideB[keep] + dropSideB;
                if (mergedSideA > maxSideSize || mergedSideB > maxSideSize)
                {
                    return false;
                }

                countSideA[keep] = mergedSideA;
                countSideB[keep] = mergedSideB;
            }

            flags[keep] = (keepSideAForgotten || dropSideAForgotten ? RelativeSideAForgotten : 0) |
                          (keepSideBForgotten || dropSideBForgotten ? RelativeSideBForgotten : 0) |
                          (keepHasSideA || dropHasSideA ? RelativeSideAPresent : 0) |
                          (keepHasSideB || dropHasSideB ? RelativeSideBPresent : 0);

            for (int i = 0; i < frontierLength; i++)
            {
                if (code[i] == Free || Representative(code, i) != drop)
                {
                    continue;
                }

                int newRelativeSide = RelativeSide(code, i) ^ flip;
                code[i] = newRelativeSide == 1 ? -(keep + 1) : keep + 1;
            }

            groupsReduced = true;
            return true;
        }

        /// <summary>
        /// Retires <paramref name="slot"/>, whose vertex has just left the frontier. If it was not its
        /// group's representative, nothing else changes. If it was, and other members remain, the group is
        /// rebased onto the smallest remaining member (flipping its metadata if that member's relative side
        /// was <c>1</c>). If it was the group's only member, the group has fully dissolved: its final
        /// per-side counts (if size-fixed) are returned to be folded into the spec's global running totals,
        /// since no frontier slot will remain to hold them.
        /// </summary>
        internal static void Forget(
            Span<int> code,
            Span<int> flags,
            Span<int> countSideA,
            Span<int> countSideB,
            int frontierLength,
            int slot,
            out bool dissolved,
            out int dissolvedCountSideA,
            out int dissolvedCountSideB)
        {
            dissolved = false;
            dissolvedCountSideA = 0;
            dissolvedCountSideB = 0;

            if (code[slot] == Free)
            {
                return;
            }

            int rep = Representative(code, slot);
            int side = RelativeSide(code, slot);
            MarkSideForgotten(flags, rep, side);

            if (rep != slot)
            {
                code[slot] = Free;
                return;
            }

            bool sizeFixed = !countSideA.IsEmpty;
            int smallestOtherMember = int.MaxValue;
            for (int j = 0; j < frontierLength; j++)
            {
                if (j == slot || code[j] == Free || Representative(code, j) != rep)
                {
                    continue;
                }

                if (j < smallestOtherMember)
                {
                    smallestOtherMember = j;
                }
            }

            if (smallestOtherMember == int.MaxValue)
            {
                dissolved = true;
                if (sizeFixed)
                {
                    dissolvedCountSideA = countSideA[slot];
                    dissolvedCountSideB = countSideB[slot];
                }

                code[slot] = Free;
                return;
            }

            int newRepFlip = RelativeSide(code, smallestOtherMember);
            int oldFlags = flags[slot];
            flags[smallestOtherMember] = newRepFlip == 0
                ? oldFlags
                : ((oldFlags & RelativeSideAForgotten) != 0 ? RelativeSideBForgotten : 0) |
                  ((oldFlags & RelativeSideBForgotten) != 0 ? RelativeSideAForgotten : 0) |
                  ((oldFlags & RelativeSideAPresent) != 0 ? RelativeSideBPresent : 0) |
                  ((oldFlags & RelativeSideBPresent) != 0 ? RelativeSideAPresent : 0);

            if (sizeFixed)
            {
                countSideA[smallestOtherMember] = newRepFlip == 0 ? countSideA[slot] : countSideB[slot];
                countSideB[smallestOtherMember] = newRepFlip == 0 ? countSideB[slot] : countSideA[slot];
            }

            for (int j = 0; j < frontierLength; j++)
            {
                if (j == slot || code[j] == Free || Representative(code, j) != rep)
                {
                    continue;
                }

                int rebasedSide = RelativeSide(code, j) ^ newRepFlip;
                code[j] = rebasedSide == 1 ? -(smallestOtherMember + 1) : smallestOtherMember + 1;
            }

            code[slot] = Free;
        }

        /// <summary>Marks that a member on relative side <paramref name="side"/> of the group at representative slot <paramref name="repSlot"/> has been forgotten.</summary>
        internal static void MarkSideForgotten(Span<int> flags, int repSlot, int side)
        {
            flags[repSlot] |= side == 0 ? RelativeSideAForgotten : RelativeSideBForgotten;
        }
    }
}

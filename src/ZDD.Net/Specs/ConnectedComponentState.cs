using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The comp-array mechanics <see cref="ConnectedSubgraphSpec"/> uses. Like
    /// <see cref="SpanningComponentState"/>, each frontier vertex's slot holds either
    /// <see cref="SlotEmpty"/> or a canonical representative code — the frontier slot with the smallest
    /// index among the component's current members, plus one. The difference is the sign: a
    /// <b>positive</b> code marks a component with no terminal vertex among its members, a
    /// <b>negative</b> code (<c>-(representativeSlot + 1)</c>) marks one that has at least one. Every
    /// member of a component carries the identical signed code, so the flag travels for free wherever the
    /// representative-rewriting loops below already walk — no separate flag array is needed, and two
    /// states describing the same partition-plus-terminal-membership always compare equal.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="SpanningComponentState"/>, merging two vertices already in the same component is
    /// not rejected here: <see cref="ConnectedSubgraphSpec"/> allows cycles, since it only asks that the
    /// terminals end up co-located, not that the result be acyclic.
    /// </remarks>
    internal static class ConnectedComponentState
    {
        /// <summary>The slot is not currently occupied by a frontier vertex.</summary>
        internal const int SlotEmpty = 0;

        /// <summary>A newly introduced vertex starts as its own singleton component.</summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="slot">The comp slot the newly introduced vertex occupies.</param>
        /// <param name="isTerminal">Whether the vertex is one of the spec's terminals.</param>
        internal static void Introduce(Span<int> state, int slot, bool isTerminal)
        {
            state[slot] = isTerminal ? -(slot + 1) : slot + 1;
        }

        /// <summary>
        /// Whether the vertices occupying <paramref name="su"/> and <paramref name="sv"/> already belong to
        /// the same component — i.e. taking the edge between them would close a cycle. Callers that must
        /// reject cycles outright (unlike this spec, which allows them) check this before calling
        /// <see cref="Merge"/>; see <see cref="SteinerTreeSpec"/>.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="su">The comp slot of one edge endpoint.</param>
        /// <param name="sv">The comp slot of the other edge endpoint.</param>
        internal static bool SameComponent(ReadOnlySpan<int> state, int su, int sv)
        {
            int codeU = state[su];
            int codeV = state[sv];
            int repU = (codeU < 0 ? -codeU : codeU) - 1;
            int repV = (codeV < 0 ? -codeV : codeV) - 1;
            return repU == repV;
        }

        /// <summary>
        /// Joins the components of the two vertices occupying <paramref name="su"/> and <paramref name="sv"/>.
        /// Always succeeds — taking an edge between two vertices already in the same component just closes
        /// a cycle, which this spec allows.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="su">The comp slot of one edge endpoint.</param>
        /// <param name="sv">The comp slot of the other edge endpoint.</param>
        /// <returns>
        /// <see langword="true"/> if the two vertices belonged to two distinct components and <b>both</b>
        /// already contained a terminal — the caller's open-terminal-component counter must drop by one,
        /// since two such components have just become one.
        /// </returns>
        internal static bool Merge(Span<int> state, int frontierLength, int su, int sv)
        {
            int codeU = state[su];
            int codeV = state[sv];
            int repU = (codeU < 0 ? -codeU : codeU) - 1;
            int repV = (codeV < 0 ? -codeV : codeV) - 1;

            if (repU == repV)
            {
                return false; // same component already: this edge just closes a cycle, nothing to merge
            }

            bool terminalU = codeU < 0;
            bool terminalV = codeV < 0;
            bool resultTerminal = terminalU || terminalV;

            int keepRep = Math.Min(repU, repV);
            int dropRep = Math.Max(repU, repV);
            int keepCode = resultTerminal ? -(keepRep + 1) : keepRep + 1;

            // Both old components' members are rewritten to the unified code, not just dropRep's: the
            // sign can flip on the keepRep side too (a non-terminal component absorbing a terminal one).
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

            return terminalU && terminalV;
        }

        /// <summary>
        /// Retires <paramref name="slot"/>, whose vertex has just left the frontier. Mirrors
        /// <see cref="SpanningComponentState.Forget"/>: if it was the component's representative and other
        /// members remain, the representative moves to the smallest remaining member's slot, carrying the
        /// same sign along — moving the representative never changes which component a vertex belongs to,
        /// or whether that component contains a terminal.
        /// </summary>
        /// <param name="state">The comp-array state.</param>
        /// <param name="frontierLength">The number of comp slots (<see cref="Graphs.FrontierManager.MaxFrontierSize"/>).</param>
        /// <param name="slot">The comp slot of the vertex leaving the frontier.</param>
        /// <param name="hadTerminal">Whether the closing component contained a terminal vertex.</param>
        /// <returns><see langword="true"/> if no other frontier vertex belongs to this component anymore —
        /// the component has closed and can never gain another member.</returns>
        internal static bool Forget(Span<int> state, int frontierLength, int slot, out bool hadTerminal)
        {
            int code = state[slot];
            int rep = (code < 0 ? -code : code) - 1;
            hadTerminal = code < 0;

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
                int newRepCode = hadTerminal ? -(smallestOtherMember + 1) : smallestOtherMember + 1;
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

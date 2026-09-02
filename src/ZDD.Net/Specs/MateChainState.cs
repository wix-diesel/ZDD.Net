using System;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The mate-array mechanics shared by <see cref="PathSpec"/>, <see cref="CycleSpec"/>,
    /// <see cref="HamiltonianPathSpec"/>, and <see cref="HamiltonianCycleSpec"/>: each frontier vertex's
    /// state-array slot holds one <c>mate</c> code describing its degree so far and, while its degree is 1,
    /// which other frontier slot the open end of its partial chain currently sits at. A code is one of:
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><see cref="SlotIsolated"/> (<c>0</c>): the vertex has degree 0 so far.</description></item>
    /// <item><description><see cref="SlotFixed"/> (<c>-1</c>): the vertex already has degree 2 — done, no
    /// further edge may touch it.</description></item>
    /// <item><description><see cref="SlotEndpointDone"/> (<c>-2</c>): the vertex has degree 1, and the
    /// *other* end of its partial chain has already been forgotten as a finished path endpoint (so the
    /// slot that end used to occupy may since have been recycled for an unrelated vertex — this code is
    /// what keeps that stale reference from being followed).</description></item>
    /// <item><description>Any other value <c>k &gt;= 1</c>: the vertex has degree 1, and the other end of
    /// its partial chain is the vertex currently occupying frontier slot <c>k - 1</c>.</description></item>
    /// </list>
    /// <para>
    /// <see cref="Splice"/> is the one piece of mate-chain bookkeeping identical across every spec built on
    /// this state: given an edge's two endpoint slots, it either extends/merges the chains passing through
    /// them, or — when the two endpoints already sit at the two ends of the very same chain — reports that
    /// taking this edge would close it into a cycle. What that closure *means* differs by spec (an error for
    /// <see cref="PathSpec"/> and <see cref="HamiltonianPathSpec"/>, the acceptance condition for
    /// <see cref="CycleSpec"/> and <see cref="HamiltonianCycleSpec"/>), so the decision is left to the
    /// caller; the forgetting rules (which final degrees are acceptable for a vertex leaving the frontier)
    /// differ by spec too and are exposed as the separate <c>Forget*</c> helpers below rather than folded
    /// into one option-laden method.
    /// </para>
    /// </remarks>
    internal static class MateChainState
    {
        /// <summary>The vertex has degree 0 so far.</summary>
        internal const int SlotIsolated = 0;

        /// <summary>The vertex already has degree 2: done, no further edge may touch it.</summary>
        internal const int SlotFixed = -1;

        /// <summary>The vertex has degree 1, and the other end of its chain is already a finished endpoint.</summary>
        internal const int SlotEndpointDone = -2;

        /// <summary>The outcome of attempting to splice an edge's two endpoints together via <see cref="Splice"/>.</summary>
        internal enum SpliceResult
        {
            /// <summary>The edge cannot be taken: one endpoint already has degree 2.</summary>
            Invalid,

            /// <summary>The two chains were extended or merged; neither endpoint reached degree 2.</summary>
            Spliced,

            /// <summary>
            /// The two endpoints were already the two ends of the same chain: taking this edge closes it
            /// into a cycle. Both endpoints are left at <see cref="SlotFixed"/> regardless of whether the
            /// caller accepts or rejects that closure.
            /// </summary>
            Closed,
        }

        /// <summary>
        /// Splices the two endpoints of an edge together: extends an open chain, merges two open chains, or
        /// (when the endpoints are already the two ends of one chain) closes it into a cycle.
        /// </summary>
        /// <param name="state">The mate-array state.</param>
        /// <param name="su">The mate slot of one edge endpoint.</param>
        /// <param name="sv">The mate slot of the other edge endpoint.</param>
        internal static SpliceResult Splice(Span<int> state, int su, int sv)
        {
            int mu = state[su];
            int mv = state[sv];

            if (mu == SlotFixed || mv == SlotFixed)
            {
                return SpliceResult.Invalid; // would give one endpoint degree 3
            }

            if ((mu >= 1 && mu - 1 == sv) || (mv >= 1 && mv - 1 == su))
            {
                // The two endpoints already share a chain: this edge closes it into a cycle.
                state[su] = SlotFixed;
                state[sv] = SlotFixed;
                return SpliceResult.Closed;
            }

            if (mu == SlotIsolated && mv == SlotIsolated)
            {
                // A brand-new two-vertex chain: u and v become each other's mate.
                state[su] = sv + 1;
                state[sv] = su + 1;
            }
            else if (mu == SlotIsolated)
            {
                // u extends v's chain; v becomes interior, u inherits v's old far end.
                state[su] = mv;
                state[sv] = SlotFixed;
                if (mv >= 1)
                {
                    state[mv - 1] = su + 1;
                }
            }
            else if (mv == SlotIsolated)
            {
                state[sv] = mu;
                state[su] = SlotFixed;
                if (mu >= 1)
                {
                    state[mu - 1] = sv + 1;
                }
            }
            else
            {
                // Two existing chains merge through u and v, which both become interior; their far ends
                // now point at each other.
                state[su] = SlotFixed;
                state[sv] = SlotFixed;
                if (mu >= 1)
                {
                    state[mu - 1] = mv;
                }

                if (mv >= 1)
                {
                    state[mv - 1] = mu;
                }
            }

            return SpliceResult.Spliced;
        }

        /// <summary>
        /// Validates and retires a path-endpoint vertex (<see cref="PathSpec"/>'s <c>s</c>/<c>t</c>, or
        /// either terminal of <see cref="HamiltonianPathSpec"/>) leaving the frontier: it must end at degree
        /// exactly 1.
        /// </summary>
        /// <returns><see langword="false"/> if the vertex's final degree is not 1.</returns>
        internal static bool ForgetTerminal(Span<int> state, int slot)
        {
            int mate = state[slot];
            if (mate == SlotIsolated || mate == SlotFixed)
            {
                return false; // an endpoint must end the build at degree exactly 1
            }

            if (mate >= 1)
            {
                state[mate - 1] = SlotEndpointDone;
            }

            state[slot] = SlotIsolated; // clear so a reused slot never carries a stale, merge-blocking code
            return true;
        }

        /// <summary>
        /// Validates and retires a vertex leaving the frontier that is allowed to end either unvisited or
        /// fully closed: used where degree 0 is a legitimate outcome (every vertex of <see cref="CycleSpec"/>,
        /// and the non-terminal vertices of <see cref="PathSpec"/>).
        /// </summary>
        /// <returns><see langword="false"/> if the vertex's final degree is 1 — a dead end that can never
        /// become a valid path interior or a closed cycle.</returns>
        internal static bool ForgetAllowIsolated(Span<int> state, int slot)
        {
            int mate = state[slot];
            if (mate != SlotIsolated && mate != SlotFixed)
            {
                return false;
            }

            state[slot] = SlotIsolated; // clear so a reused slot never carries a stale, merge-blocking code
            return true;
        }

        /// <summary>
        /// Validates and retires a vertex leaving the frontier that must have been visited: used by every
        /// vertex of <see cref="HamiltonianCycleSpec"/> and the non-terminal vertices of
        /// <see cref="HamiltonianPathSpec"/>, where a vertex that never gained an edge (degree 0) breaks the
        /// "every vertex is on the path/cycle" requirement.
        /// </summary>
        /// <returns><see langword="false"/> if the vertex's final degree is not exactly 2.</returns>
        internal static bool ForgetRequireVisited(Span<int> state, int slot)
        {
            int mate = state[slot];
            if (mate != SlotFixed)
            {
                return false;
            }

            state[slot] = SlotIsolated; // clear so a reused slot never carries a stale, merge-blocking code
            return true;
        }
    }
}

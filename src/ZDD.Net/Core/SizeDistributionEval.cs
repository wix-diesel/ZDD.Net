using System;
using System.Numerics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Evaluator that counts sets in the family grouped by set size (cardinality of each set).
    /// Backs <see cref="Zdd.CountBySize"/>.
    /// </summary>
    /// <remarks>
    /// The value at each node is an array where index <c>k</c> holds the count of sets of size
    /// <c>k</c>, sized to the largest set actually reachable from that node (not the manager's
    /// variable count). Recurrence: <c>result[k] = lo[k] + hi[k - 1]</c> (the hi side shifts by
    /// one since including the item grows the set by one element). Costs
    /// <c>O(node count × max size)</c> time and memory; use <see cref="CardinalityEval"/> if only
    /// the total count is needed. Returned arrays may be shared between nodes and are never
    /// mutated by this evaluator.
    /// </remarks>
    public readonly struct SizeDistributionEval : IDdEval<BigInteger[]>
    {
        /// <inheritdoc/>
        public BigInteger[] EvalTerminal(bool isTrue) =>
            isTrue ? new BigInteger[] { BigInteger.One } : Array.Empty<BigInteger>();

        /// <inheritdoc/>
        public BigInteger[] EvalNode(int item, BigInteger[] lo, BigInteger[] hi)
        {
            ArgumentNullException.ThrowIfNull(lo);
            ArgumentNullException.ThrowIfNull(hi);

            // If hi is empty, lo alone is already the answer (hi is the side that needs shifting).
            if (hi.Length == 0)
            {
                return lo;
            }

            BigInteger[] result = new BigInteger[Math.Max(lo.Length, hi.Length + 1)];

            for (int size = 0; size < lo.Length; size++)
            {
                result[size] = lo[size];
            }

            // The item-included side shifts by one: it adds one to each set's size.
            for (int size = 0; size < hi.Length; size++)
            {
                result[size + 1] += hi[size];
            }

            return result;
        }
    }
}

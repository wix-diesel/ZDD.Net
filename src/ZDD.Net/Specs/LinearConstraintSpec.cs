using System;
using ZDD.Net.Frontier;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of subsets satisfying <c>Σ a[i] x[i] {&lt;=, ==, &gt;=} b</c>, where <c>x[i]</c> is
    /// 1 if item <c>i</c> is chosen and 0 otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: the weighted sum accumulated so far (a <c>long</c>, to keep coefficient sums from
    /// overflowing). Which items contributed to it never matters again — only the running total does —
    /// so the sum alone determines every later decision.
    /// </para>
    /// <para>
    /// <b>Negative coefficients</b>: allowed. Pruning needs, at each point, the best and worst totals
    /// still reachable from the remaining items; with only non-negative coefficients "best" would mean
    /// "take everything left" and "worst" would mean "take nothing more", but a negative coefficient can
    /// flip that per item. The constructor precomputes, for every position, the suffix sum of the
    /// remaining items' positive parts (the most the sum can still grow by) and of their negative parts
    /// (the most it can still shrink by), and <see cref="GetChild"/> uses both together — so a branch is
    /// pruned to <see cref="DdResult.False"/> the moment neither extreme can satisfy the operator anymore,
    /// regardless of the sign mix.
    /// </para>
    /// </remarks>
    public readonly struct LinearConstraintSpec : IDdSpec<long>
    {
        private readonly int[] _coefficients;
        private readonly LinearConstraintOperator _op;
        private readonly long _bound;

        // _suffixMaxSum[i] / _suffixMinSum[i]: the most / least Σ a[j] x[j] for j in [i, ItemCount) can
        // add, over j = i .. ItemCount - 1. Length ItemCount + 1; the last entry is 0 (no items left).
        private readonly long[] _suffixMaxSum;
        private readonly long[] _suffixMinSum;

        /// <summary>Creates a spec enforcing <c>Σ coefficients[i] x[i] {op} bound</c>.</summary>
        /// <param name="coefficients">
        /// The per-item coefficients <c>a[i]</c>; may contain negatives. Copied, so later mutating the
        /// array passed in has no effect on the spec.
        /// </param>
        /// <param name="op">The comparison to enforce.</param>
        /// <param name="bound">The bound <c>b</c>.</param>
        public LinearConstraintSpec(int[] coefficients, LinearConstraintOperator op, long bound)
        {
            ArgumentNullException.ThrowIfNull(coefficients);

            _coefficients = (int[])coefficients.Clone();
            _op = op;
            _bound = bound;

            int n = _coefficients.Length;
            _suffixMaxSum = new long[n + 1];
            _suffixMinSum = new long[n + 1];

            for (int i = n - 1; i >= 0; i--)
            {
                long c = _coefficients[i];
                _suffixMaxSum[i] = _suffixMaxSum[i + 1] + Math.Max(c, 0);
                _suffixMinSum[i] = _suffixMinSum[i + 1] + Math.Min(c, 0);
            }
        }

        /// <summary>The number of items, i.e. the length of the coefficient array.</summary>
        public int ItemCount => _coefficients.Length;

        /// <inheritdoc/>
        public int GetRoot(ref long sum)
        {
            sum = 0;
            return ItemCount == 0
                ? (Infeasible(0, 0) ? DdResult.False : DdResult.True)
                : ItemCount;
        }

        /// <inheritdoc/>
        public int GetChild(ref long sum, int level, int value)
        {
            int idx = ItemCount - level;
            sum += (long)_coefficients[idx] * value;

            int remaining = level - 1;
            int next = idx + 1; // = ItemCount - remaining
            long finalMin = sum + _suffixMinSum[next];
            long finalMax = sum + _suffixMaxSum[next];

            if (Infeasible(finalMin, finalMax))
            {
                return DdResult.False;
            }

            // At the last item, next == ItemCount so finalMin == finalMax == sum, and Infeasible(sum, sum)
            // is exactly "sum does not satisfy the operator" for all three operators — so not being
            // infeasible already means the operator holds and the rest (there is none) can be excluded.
            return remaining == 0 ? DdResult.True : remaining;
        }

        /// <summary>
        /// Whether every reachable final sum in <c>[finalMin, finalMax]</c> fails the operator — i.e.
        /// no choice for the remaining items could ever satisfy it, so the branch can be pruned now.
        /// </summary>
        private bool Infeasible(long finalMin, long finalMax) => _op switch
        {
            LinearConstraintOperator.LessOrEqual => finalMin > _bound,
            LinearConstraintOperator.GreaterOrEqual => finalMax < _bound,
            LinearConstraintOperator.Equal => finalMax < _bound || finalMin > _bound,
            _ => throw new ArgumentOutOfRangeException(nameof(_op), _op, "Unknown operator."),
        };

        /// <inheritdoc/>
        public bool StateEquals(in long left, in long right) => left == right;

        /// <inheritdoc/>
        public int StateHashCode(in long state) => state.GetHashCode();
    }
}

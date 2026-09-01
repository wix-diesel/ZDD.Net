using System;
using ZDD.Net.Frontier;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of subsets whose total weight fits a capacity: <c>Σ weights[i] x[i] &lt;= capacity</c>.
    /// The special case of <see cref="LinearConstraintSpec"/> with <see cref="LinearConstraintOperator.LessOrEqual"/>,
    /// kept as its own spec because "remaining capacity" reads more directly than "sum so far" and lets
    /// the state be canonicalized further (see below).
    /// </summary>
    /// <remarks>
    /// <b>State</b>: the capacity still available (a <c>long</c>): <c>capacity - Σ</c> weights chosen so
    /// far. That is enough on its own — no future decision needs to know which items produced it, only
    /// how much room is left. It is additionally clamped down to the total weight of the items not yet
    /// decided: once the remaining capacity already covers taking every one of them, any further slack
    /// makes no difference to any future branch, so clamping merges all "plenty of room left" states into
    /// one instead of letting the diagram fan out over the exact amount of surplus room.
    /// </remarks>
    public readonly struct KnapsackSpec : IDdSpec<long>
    {
        private readonly int[] _weights;
        private readonly long _capacity;

        // _suffixWeightSum[i]: total weight of items i .. ItemCount - 1. Length ItemCount + 1; last entry 0.
        private readonly long[] _suffixWeightSum;

        /// <summary>Creates a spec accepting subsets whose weights sum to at most <paramref name="capacity"/>.</summary>
        /// <param name="weights">
        /// The per-item weights; must all be non-negative. Copied, so later mutating the array passed
        /// in has no effect on the spec.
        /// </param>
        /// <param name="capacity">
        /// The capacity. Negative is accepted and simply describes the empty family (not even the empty
        /// set fits a negative capacity), matching how <see cref="CardinalitySpec"/> treats <c>min &gt; n</c>.
        /// </param>
        public KnapsackSpec(int[] weights, long capacity)
        {
            ArgumentNullException.ThrowIfNull(weights);

            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(weights), weights[i], "Weights must be non-negative.");
                }
            }

            _weights = (int[])weights.Clone();
            _capacity = capacity;

            int n = _weights.Length;
            _suffixWeightSum = new long[n + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                _suffixWeightSum[i] = _suffixWeightSum[i + 1] + _weights[i];
            }
        }

        /// <summary>The number of items, i.e. the length of the weight array.</summary>
        public int ItemCount => _weights.Length;

        /// <inheritdoc/>
        public int GetRoot(ref long remainingCapacity)
        {
            if (_capacity < 0)
            {
                remainingCapacity = 0;
                return DdResult.False;
            }

            remainingCapacity = Math.Min(_capacity, _suffixWeightSum[0]);
            return ItemCount == 0 ? DdResult.True : ItemCount;
        }

        /// <inheritdoc/>
        public int GetChild(ref long remainingCapacity, int level, int value)
        {
            int idx = ItemCount - level;

            if (value == 1)
            {
                remainingCapacity -= _weights[idx];
                if (remainingCapacity < 0)
                {
                    return DdResult.False;
                }
            }

            int remaining = level - 1;
            int next = idx + 1; // = ItemCount - remaining
            remainingCapacity = Math.Min(remainingCapacity, _suffixWeightSum[next]);

            return remaining == 0 ? DdResult.True : remaining;
        }

        /// <inheritdoc/>
        public bool StateEquals(in long left, in long right) => left == right;

        /// <inheritdoc/>
        public int StateHashCode(in long state) => state.GetHashCode();
    }
}

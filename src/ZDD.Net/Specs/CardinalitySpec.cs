using System;
using ZDD.Net.Frontier;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The family of subsets whose size lies in <c>[min, max]</c>. <c>min == max</c> means "exactly k".
    /// </summary>
    /// <remarks>
    /// <b>State</b>: the count of items included so far (an <c>int</c>). Which items were chosen never
    /// affects whether the final count lands in range, so the count alone is enough — states with the
    /// same running count are always interchangeable from that point on. The pruning in
    /// <see cref="GetChild"/> keeps every live state in <c>[0, max]</c> (anything higher is cut
    /// immediately), so the diagram's width is at most <c>max + 1</c>; it only approaches <c>max - min + 1</c>
    /// once enough items have been decided that counts below <c>min</c> can no longer catch up and get
    /// pruned too.
    /// </remarks>
    public readonly struct CardinalitySpec : IDdSpec<int>
    {
        private readonly int _itemCount;
        private readonly int _min;
        private readonly int _max;

        /// <summary>Creates a spec accepting subsets of size <c>[min, max]</c> over <paramref name="itemCount"/> items.</summary>
        /// <param name="itemCount">The number of items; must be non-negative.</param>
        /// <param name="min">The minimum accepted size; must be non-negative.</param>
        /// <param name="max">The maximum accepted size; must be at least <paramref name="min"/>.</param>
        /// <remarks>
        /// <paramref name="min"/> greater than <paramref name="itemCount"/> is not rejected here: it is
        /// a legitimate way to describe the empty family, and the branch-and-bound pruning in
        /// <see cref="GetChild"/> collapses every branch to <see cref="DdResult.False"/> on its own.
        /// </remarks>
        public CardinalitySpec(int itemCount, int min, int max)
        {
            if (itemCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemCount), itemCount, "The item count must be non-negative.");
            }

            if (min < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(min), min, "The minimum must be non-negative.");
            }

            if (max < min)
            {
                throw new ArgumentOutOfRangeException(nameof(max), max, "The maximum must be at least the minimum.");
            }

            _itemCount = itemCount;
            _min = min;
            _max = max;
        }

        /// <inheritdoc/>
        public int GetRoot(ref int taken)
        {
            taken = 0;
            if (_itemCount == 0)
            {
                return Feasible(0, 0) ? DdResult.True : DdResult.False;
            }

            return _itemCount;
        }

        /// <inheritdoc/>
        public int GetChild(ref int taken, int level, int value)
        {
            taken += value;
            int remaining = level - 1;

            if (!Feasible(taken, remaining))
            {
                // Either the max is already blown, or even taking every remaining item can't reach the min.
                return DdResult.False;
            }

            if (taken == _max || remaining == 0)
            {
                // taken == _max: any further "include" would exceed it, so the rest must all be excluded,
                // which is exactly what returning True does (zero-suppression). taken is already >= _min
                // here because Feasible confirmed taken + remaining >= _min while taken can only grow.
                // remaining == 0: this was the last item, and Feasible already confirmed taken is in range.
                return DdResult.True;
            }

            return remaining;
        }

        /// <summary>Whether <paramref name="taken"/>, plus up to <paramref name="remaining"/> more, can still land in <c>[min, max]</c>.</summary>
        private bool Feasible(int taken, int remaining) => taken <= _max && taken + remaining >= _min;

        /// <inheritdoc/>
        public bool StateEquals(in int left, in int right) => left == right;

        /// <inheritdoc/>
        public int StateHashCode(in int state) => state;
    }
}

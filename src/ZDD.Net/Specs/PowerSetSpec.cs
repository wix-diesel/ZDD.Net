using System;
using ZDD.Net.Frontier;

namespace ZDD.Net.Specs
{
    /// <summary>
    /// The power set 2^S: every subset of the item set, with no constraint at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State</b>: none. Whether an item is later included or excluded never depends on what came
    /// before, so there is nothing to carry between levels. <c>byte</c> is used only because
    /// <see cref="IDdSpec{TState}"/> needs some <c>TState</c>; the value is never read.
    /// </para>
    /// <para>
    /// Both branches of every level lead to the same continuation, so every level collapses to a
    /// single state — the resulting <see cref="Core.Zdd"/> has width 1 at every level.
    /// </para>
    /// </remarks>
    public readonly struct PowerSetSpec : IDdSpec<byte>
    {
        private readonly int _itemCount;

        /// <summary>Creates a spec over <paramref name="itemCount"/> items, all free.</summary>
        /// <param name="itemCount">The number of items; must be non-negative.</param>
        public PowerSetSpec(int itemCount)
        {
            if (itemCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemCount), itemCount, "The item count must be non-negative.");
            }

            _itemCount = itemCount;
        }

        /// <inheritdoc/>
        public int GetRoot(ref byte state)
        {
            state = 0;
            return _itemCount == 0 ? DdResult.True : _itemCount;
        }

        /// <inheritdoc/>
        public int GetChild(ref byte state, int level, int value) =>
            level == 1 ? DdResult.True : level - 1;

        /// <inheritdoc/>
        public bool StateEquals(in byte left, in byte right) => true;

        /// <inheritdoc/>
        public int StateHashCode(in byte state) => 0;
    }
}

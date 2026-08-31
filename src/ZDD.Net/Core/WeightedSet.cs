using System;
using System.Text;

namespace ZDD.Net.Core
{
    /// <summary>A set paired with its weight, as returned by weight optimization.</summary>
    /// <typeparam name="TWeight">The weight type.</typeparam>
    /// <remarks>
    /// Bundles the optimal value with the optimal set since reconstructing the set from the DP
    /// table alone still takes only O(variable count). <see cref="Items"/>'s array is owned by
    /// this result (not shared with the family); the same result always returns the same array
    /// instance, but a new optimization call returns a fresh one.
    /// </remarks>
    public readonly struct WeightedSet<TWeight>
    {
        private readonly int[]? _items;

        /// <summary>Pairs a set with its weight.</summary>
        /// <param name="weight">The set's weight.</param>
        /// <param name="items">Item indices in the set, ascending and unique.</param>
        internal WeightedSet(TWeight weight, int[] items)
        {
            Weight = weight;
            _items = items;
        }

        /// <summary>The set's weight (sum of its items' weights).</summary>
        public TWeight Weight { get; }

        /// <summary>Item indices in this set, ascending and unique; empty if the set is empty.</summary>
        public int[] Items => _items ?? Array.Empty<int>();

        /// <summary>The number of elements in the set.</summary>
        public int Size => _items?.Length ?? 0;

        /// <summary>Formats as <c>{0, 2} (weight 7)</c>.</summary>
        public override string ToString()
        {
            StringBuilder text = new StringBuilder();
            int[] items = Items;

            if (items.Length == 0)
            {
                text.Append('∅');
            }
            else
            {
                text.Append('{');

                for (int i = 0; i < items.Length; i++)
                {
                    if (i > 0)
                    {
                        text.Append(", ");
                    }

                    text.Append(items[i]);
                }

                text.Append('}');
            }

            return text.Append(" (weight ").Append(Weight).Append(')').ToString();
        }
    }
}

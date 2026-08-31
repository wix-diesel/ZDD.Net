using System;
using System.Numerics;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Ranking/unranking (<see cref="Zdd.ElementAt(BigInteger, ZddEnumerationOrder)"/> /
    /// <see cref="Zdd.IndexOf(System.Collections.Generic.IEnumerable{int}, ZddEnumerationOrder)"/>)
    /// and uniform sampling (<see cref="Zdd.Sample(Random)"/>).
    /// </summary>
    /// <remarks>
    /// Uses per-node subtree cardinalities (<see cref="CardinalityTable"/>) to reach the
    /// k-th set with a single root-to-leaf descent, instead of enumerating k sets.
    /// Ordering matches <see cref="Zdd.Sets(ZddEnumerationOrder)"/> for the same
    /// <see cref="ZddEnumerationOrder"/>. Cost is O(node count) to build the table plus
    /// O(variable count) per rank/unrank; iterative, no recursion.
    /// </remarks>
    internal static class SetRanking
    {
        /// <summary>Initial size of the path scratch buffer; doubles when it fills up.</summary>
        private const int InitialPathCapacity = 16;

        /// <summary>Returns the <paramref name="index"/>-th (0-based) set in the family (unranking).</summary>
        /// <param name="manager">Manager that owns the family.</param>
        /// <param name="rootId">Root node ID of the family.</param>
        /// <param name="index">Rank of the set to retrieve; must be within the family's cardinality.</param>
        /// <param name="order">Ordering to rank by, matching enumeration order.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="order"/> is undefined, or <paramref name="index"/> is out of range.
        /// </exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> is disposed.</exception>
        public static int[] ElementAt(ZddManager manager, int rootId, BigInteger index, ZddEnumerationOrder order)
        {
            SetEnumeration.EnsureDefinedOrder(order);

            CardinalityTable table = CardinalityTable.Build(manager, rootId);
            BigInteger count = table.CountOf(rootId);

            if (index < BigInteger.Zero || index >= count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(index),
                    count.IsZero
                        ? $"The family is empty, so no '{nameof(index)}' is valid; it was {index}."
                        : $"'{nameof(index)}' must be in the range 0..{count - BigInteger.One}, but was {index}.");
            }

            int[] path = new int[InitialPathCapacity];
            return Unrank(manager, table, rootId, index, order == ZddEnumerationOrder.Lexicographic, ref path);
        }

        /// <summary>Returns the rank of the set represented by <paramref name="items"/>, or -1 if not in the family.</summary>
        /// <param name="manager">Manager that owns the family.</param>
        /// <param name="rootId">Root node ID of the family.</param>
        /// <param name="items">Item indices of the set to look up; order and duplicates don't matter.</param>
        /// <param name="order">Ordering to rank by, matching enumeration order.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="order"/> is undefined, or <paramref name="items"/> contains an out-of-range item index.
        /// </exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> is disposed.</exception>
        public static BigInteger IndexOf(
            ZddManager manager,
            int rootId,
            ReadOnlySpan<int> items,
            ZddEnumerationOrder order)
        {
            SetEnumeration.EnsureDefinedOrder(order);

            NodeTable nodes = manager.Table.Nodes;
            QueryOperations.EnsureItemsInRange(manager, items);

            int[] wanted = SortedDistinct(items);

            // Membership must hold before ranking; a set outside the family has no rank.
            if (!QueryOperations.ContainsSorted(manager, rootId, wanted))
            {
                return BigInteger.MinusOne;
            }

            CardinalityTable table = CardinalityTable.Build(manager, rootId);

            return order == ZddEnumerationOrder.Lexicographic
                ? LexicographicRank(manager, nodes, table, rootId, wanted)
                : DefaultRank(manager, nodes, table, rootId, wanted);
        }

        /// <summary>Picks one set from the family uniformly at random.</summary>
        /// <param name="manager">Manager that owns the family.</param>
        /// <param name="rootId">Root node ID of the family.</param>
        /// <param name="random">Random number source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The family is empty.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> is disposed.</exception>
        public static int[] Sample(ZddManager manager, int rootId, Random random)
        {
            ThrowHelper.ThrowIfNull(random, nameof(random));

            CardinalityTable table = CardinalityTable.Build(manager, rootId);
            BigInteger count = table.CountOf(rootId);
            EnsureNotEmpty(count);

            UniformBigInteger uniform = new UniformBigInteger(count);
            int[] path = new int[InitialPathCapacity];

            return Unrank(manager, table, rootId, uniform.Next(random), lexicographic: false, ref path);
        }

        /// <summary>Picks <paramref name="count"/> sets from the family uniformly at random, with replacement.</summary>
        /// <param name="manager">Manager that owns the family.</param>
        /// <param name="rootId">Root node ID of the family.</param>
        /// <param name="count">Number of sets to draw; must be non-negative.</param>
        /// <param name="random">Random number source.</param>
        /// <remarks>Draws are independent, so the same set may be returned more than once.</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        /// <exception cref="InvalidOperationException">The family is empty.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> is disposed.</exception>
        public static int[][] Sample(ZddManager manager, int rootId, int count, Random random)
        {
            ThrowHelper.ThrowIfNegative(count, nameof(count));
            ThrowHelper.ThrowIfNull(random, nameof(random));

            CardinalityTable table = CardinalityTable.Build(manager, rootId);
            BigInteger cardinality = table.CountOf(rootId);
            EnsureNotEmpty(cardinality);

            UniformBigInteger uniform = new UniformBigInteger(cardinality);
            int[] path = new int[InitialPathCapacity];
            int[][] result = new int[count][];

            for (int i = 0; i < count; i++)
            {
                result[i] = Unrank(manager, table, rootId, uniform.Next(random), lexicographic: false, ref path);
            }

            return result;
        }

        /// <summary>Builds the <paramref name="index"/>-th set by descending a single path from the root.</summary>
        /// <remarks>
        /// <paramref name="path"/> is a caller-reused scratch buffer, passed by <c>ref</c> so it can
        /// grow in place; the returned array is always freshly allocated.
        /// </remarks>
        private static int[] Unrank(
            ZddManager manager,
            CardinalityTable table,
            int rootId,
            BigInteger index,
            bool lexicographic,
            ref int[] path)
        {
            NodeTable nodes = manager.Table.Nodes;

            int length = 0;
            int id = rootId;

            while (!NodeTable.IsTerminal(id))
            {
                if (!lexicographic)
                {
                    // Default order: all sets under the lo-branch come first.
                    ZddNode node = nodes[id];
                    BigInteger loCount = table.CountOf(node.Lo);

                    if (index < loCount)
                    {
                        id = node.Lo;
                        continue;
                    }

                    index -= loCount;
                    Append(ref path, ref length, manager.ItemOf(node.Level));
                    id = node.Hi;
                    continue;
                }

                // Lexicographic order: the empty set, if present, comes first.
                if (table.HasEmptySet(id))
                {
                    if (index.IsZero)
                    {
                        return path.AsSpan(0, length).ToArray();
                    }

                    index -= BigInteger.One;
                }

                // Otherwise walk the lo-chain to find the block (hi-branch) holding this rank.
                while (true)
                {
                    if (NodeTable.IsTerminal(id))
                    {
                        ThrowHelper.ThrowInvalidOperationException(
                            $"The rank {index} ran past the end of the family while descending; the cardinality table and the diagram disagree.");
                    }

                    ZddNode node = nodes[id];
                    BigInteger hiCount = table.CountOf(node.Hi);

                    if (index < hiCount)
                    {
                        Append(ref path, ref length, manager.ItemOf(node.Level));
                        id = node.Hi;
                        break;
                    }

                    index -= hiCount;
                    id = node.Lo;
                }
            }

            if (id != NodeTable.Top)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    "The descent ended at the bottom terminal, which holds no set; the cardinality table and the diagram disagree.");
            }

            return path.AsSpan(0, length).ToArray();
        }

        /// <summary>Computes the rank under default order (<see cref="ZddEnumerationOrder.Default"/>).</summary>
        /// <remarks>
        /// Mirrors <see cref="QueryOperations.Contains"/>'s descent, accumulating the lo-branch
        /// cardinality each time a 1-branch is taken.
        /// </remarks>
        private static BigInteger DefaultRank(
            ZddManager manager,
            NodeTable nodes,
            CardinalityTable table,
            int rootId,
            int[] wanted)
        {
            BigInteger rank = BigInteger.Zero;
            int next = 0;
            int id = rootId;

            while (!NodeTable.IsTerminal(id))
            {
                ZddNode node = nodes[id];
                int item = manager.ItemOf(node.Level);

                if (next < wanted.Length && wanted[next] == item)
                {
                    rank += table.CountOf(node.Lo);
                    next++;
                    id = node.Hi;
                    continue;
                }

                id = node.Lo;
            }

            return rank;
        }

        /// <summary>Computes the rank under lexicographic order (<see cref="ZddEnumerationOrder.Lexicographic"/>).</summary>
        /// <remarks>Mirrors <see cref="Unrank"/> in reverse, one block at a time.</remarks>
        private static BigInteger LexicographicRank(
            ZddManager manager,
            NodeTable nodes,
            CardinalityTable table,
            int rootId,
            int[] wanted)
        {
            BigInteger rank = BigInteger.Zero;
            int next = 0;
            int id = rootId;

            while (!NodeTable.IsTerminal(id))
            {
                if (next == wanted.Length)
                {
                    // The remaining wanted items are empty, so this subfamily's first set is the answer.
                    return rank;
                }

                if (table.HasEmptySet(id))
                {
                    rank += BigInteger.One;
                }

                while (true)
                {
                    ZddNode node = nodes[id];
                    int item = manager.ItemOf(node.Level);

                    if (item == wanted[next])
                    {
                        next++;
                        id = node.Hi;
                        break;
                    }

                    // Sets starting with this item precede the wanted set.
                    rank += table.CountOf(node.Hi);
                    id = node.Lo;
                }
            }

            return rank;
        }

        /// <summary>Sorts items ascending and removes duplicates.</summary>
        private static int[] SortedDistinct(ReadOnlySpan<int> items)
        {
            if (items.Length == 0)
            {
                return Array.Empty<int>();
            }

            int[] sorted = items.ToArray();
            Array.Sort(sorted);

            int length = 1;
            for (int i = 1; i < sorted.Length; i++)
            {
                if (sorted[i] != sorted[length - 1])
                {
                    sorted[length++] = sorted[i];
                }
            }

            return length == sorted.Length ? sorted : sorted.AsSpan(0, length).ToArray();
        }

        private static void EnsureNotEmpty(BigInteger count)
        {
            if (count.IsZero)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    "The family holds no set, so there is nothing to sample; check IsEmpty before sampling.");
            }
        }

        private static void Append(ref int[] buffer, ref int length, int value)
        {
            if (length == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            buffer[length++] = value;
        }
    }
}

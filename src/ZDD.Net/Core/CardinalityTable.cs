using System;
using System.Collections.Generic;
using System.Numerics;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// The number of sets in a family, tracked <b>per node</b>. Used by
    /// <see cref="SetRanking"/> to pick which branch to descend into during
    /// unranking, ranking, and sampling.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Zdd.Count"/> (a single root value), this keeps the count for every
    /// node so ranking can ask "how many sets lie below this node's 0-edge" along a path.
    /// Also tracks whether each subfamily contains the empty set (<see cref="HasEmptySet"/>),
    /// needed for lexicographic ordering where the empty set sorts first.
    /// The table is rebuilt on demand rather than cached on the manager, since node IDs can be
    /// invalidated by <see cref="ZddManager.Collect()"/>. Traversal uses an explicit stack, not recursion, since ZDD
    /// depth equals the variable count and a naive recursive walk can overflow the stack.
    /// </remarks>
    internal sealed class CardinalityTable
    {
        /// <summary>Initial depth of the explicit stack; doubles when it runs out.</summary>
        private const int InitialStackCapacity = 32;

        /// <summary>Non-terminal node ID to its subfamily info. Terminals aren't stored (trivial values).</summary>
        private readonly Dictionary<int, Entry> _entries;

        private CardinalityTable(Dictionary<int, Entry> entries) => _entries = entries;

        /// <summary>Builds the subfamily-cardinality table for every node reachable from <paramref name="rootId"/>.</summary>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">The family's root node ID.</param>
        /// <remarks>
        /// Postorder traversal, one addition per node; cost is proportional to node count,
        /// not to the (possibly astronomically larger) number of sets.
        /// </remarks>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static CardinalityTable Build(ZddManager manager, int rootId)
        {
            NodeTable nodes = manager.Table.Nodes;

            Dictionary<int, Entry> entries = new Dictionary<int, Entry>();

            if (NodeTable.IsTerminal(rootId))
            {
                return new CardinalityTable(entries);
            }

            // Two kinds of stack entries, told apart by sign: non-negative means "not yet
            // computed", negative (bit-complemented) means "children are done, combine now".
            // Non-terminal IDs are always >= 2, so complementing always yields a negative value.
            int[] stack = new int[InitialStackCapacity];
            int top = 0;

            Push(ref stack, ref top, rootId);

            while (top > 0)
            {
                int item = stack[--top];

                if (item < 0)
                {
                    int id = ~item;
                    int lo;
                    int hi;
                    {
                        ref ZddNode node = ref nodes[id];
                        lo = node.Lo;
                        hi = node.Hi;
                    }

                    // Count = sets excluding this item + sets including it. The empty set can
                    // only live on the 0-edge side, so that flag is simply inherited from it.
                    entries[id] = new Entry(
                        CountIn(entries, lo) + CountIn(entries, hi),
                        HasEmptySetIn(entries, lo));
                    continue;
                }

                // Another parent already handled this node.
                if (entries.ContainsKey(item))
                {
                    continue;
                }

                int childLo;
                int childHi;
                {
                    ref ZddNode node = ref nodes[item];
                    childLo = node.Lo;
                    childHi = node.Hi;
                }

                // Push self first, then unresolved children on top (LIFO, so children finish first).
                Push(ref stack, ref top, ~item);

                if (!NodeTable.IsTerminal(childLo) && !entries.ContainsKey(childLo))
                {
                    Push(ref stack, ref top, childLo);
                }

                if (!NodeTable.IsTerminal(childHi) && !entries.ContainsKey(childHi))
                {
                    Push(ref stack, ref top, childHi);
                }
            }

            return new CardinalityTable(entries);
        }

        /// <summary>Number of sets in the subfamily rooted at <paramref name="id"/>.</summary>
        /// <param name="id">A node ID reachable from the root this table was built for (terminals allowed).</param>
        public BigInteger CountOf(int id) => CountIn(_entries, id);

        /// <summary>Whether the subfamily rooted at <paramref name="id"/> contains the empty set.</summary>
        /// <param name="id">A node ID reachable from the root this table was built for (terminals allowed).</param>
        public bool HasEmptySet(int id) => HasEmptySetIn(_entries, id);

        private static BigInteger CountIn(Dictionary<int, Entry> entries, int id)
        {
            if (NodeTable.IsTerminal(id))
            {
                return id == NodeTable.Top ? BigInteger.One : BigInteger.Zero;
            }

            return EntryIn(entries, id).Count;
        }

        private static bool HasEmptySetIn(Dictionary<int, Entry> entries, int id)
        {
            if (NodeTable.IsTerminal(id))
            {
                return id == NodeTable.Top;
            }

            return EntryIn(entries, id).HasEmptySet;
        }

        private static Entry EntryIn(Dictionary<int, Entry> entries, int id)
        {
            if (!entries.TryGetValue(id, out Entry entry))
            {
                // Postorder means children are always resolved before parents; getting here
                // means the traversal or caller is broken.
                ThrowHelper.ThrowInvalidOperationException(
                    $"The cardinality of node {id} was read before it was computed.");
            }

            return entry;
        }

        private static void Push(ref int[] stack, ref int top, int item)
        {
            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = item;
        }

        /// <summary>Per-node data stored in the table.</summary>
        private readonly struct Entry
        {
            public Entry(BigInteger count, bool hasEmptySet)
            {
                Count = count;
                HasEmptySet = hasEmptySet;
            }

            /// <summary>Number of sets in the subfamily rooted at this node.</summary>
            public BigInteger Count { get; }

            /// <summary>Whether the subfamily rooted at this node contains the empty set.</summary>
            public bool HasEmptySet { get; }
        }
    }
}

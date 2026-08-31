using System;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Implements the boolean-only queries that never build a result family
    /// (<see cref="Zdd.Contains(System.Collections.Generic.IEnumerable{int})"/> /
    /// <see cref="Zdd.IsSubsetOf"/> / <see cref="Zdd.Overlaps"/>).
    /// </summary>
    /// <remarks>
    /// <c>Overlaps</c> and <c>IsSubsetOf</c> each reduce to a single-connective tree of sub-answers
    /// (OR for Overlaps, AND for IsSubsetOf), so a decisive value short-circuits the whole search
    /// with no combine step needed. Visited pairs are memoized in the workspace's result table to
    /// avoid revisiting shared nodes exponentially many times; pairs that resolve trivially (one
    /// side is the ⊤ terminal) are memoized separately via <see cref="HasEmptySet(NodeTable, OperationWorkspace, ZddOperation, int)"/>
    /// to avoid re-walking the 0-branch chain on every level (a real O(n²) blowup, see issue #90).
    /// The walk is iterative — ZDD depth equals variable count, so recursion could overflow the stack.
    /// </remarks>
    internal static class QueryOperations
    {
        /// <summary>Checks whether the set of <paramref name="items"/> belongs to the family.</summary>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">Root node ID of the family.</param>
        /// <param name="items">Item indices of the set to check; any order, duplicates allowed.</param>
        /// <remarks>Sorts <paramref name="items"/> then walks a single root-to-terminal path (see <see cref="ContainsSorted"/>) — O(variable count) plus O(k log k) for the sort.</remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="items"/> has an out-of-range item index.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static bool Contains(ZddManager manager, int rootId, ReadOnlySpan<int> items)
        {
            EnsureItemsInRange(manager, items);

            // Sorted ascending so it can advance in lockstep with the ZDD's root-to-leaf item order.
            int[] sorted = items.ToArray();
            Array.Sort(sorted);

            return ContainsSorted(manager, rootId, sorted);
        }

        /// <summary><see cref="Contains"/>, assuming the set is already sorted ascending.</summary>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">Root node ID of the family.</param>
        /// <param name="sortedItems">Item indices, ascending and already range-checked (<see cref="EnsureItemsInRange"/>); duplicates allowed.</param>
        /// <remarks>Lets callers who already have a sorted set (e.g. <see cref="SetRanking"/>) skip re-sorting.</remarks>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static bool ContainsSorted(ZddManager manager, int rootId, ReadOnlySpan<int> sortedItems)
        {
            NodeTable nodes = manager.Table.Nodes;

            if (sortedItems.Length == 0)
            {
                return HasEmptySet(nodes, rootId);
            }

            int next = 0;
            int id = rootId;

            while (!NodeTable.IsTerminal(id))
            {
                ref ZddNode node = ref nodes[id];
                int item = manager.ItemOf(node.Level);

                if (next < sortedItems.Length && sortedItems[next] < item)
                {
                    // No node below branches on this item, so no set down here contains it.
                    return false;
                }

                if (next < sortedItems.Length && sortedItems[next] == item)
                {
                    // Collapse duplicates of the same item.
                    do
                    {
                        next++;
                    }
                    while (next < sortedItems.Length && sortedItems[next] == item);

                    id = node.Hi;
                }
                else
                {
                    id = node.Lo;
                }
            }

            // Leftover items at the terminal mean we followed the wrong set.
            return id == NodeTable.Top && next == sortedItems.Length;
        }

        /// <summary>Verifies that every item index lies within this manager's universe.</summary>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="items">Item indices to check.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="items"/> has an out-of-range item index.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static void EnsureItemsInRange(ZddManager manager, ReadOnlySpan<int> items)
        {
            _ = manager.Table; // throws ObjectDisposedException up front if disposed

            foreach (int item in items)
            {
                _ = manager.LevelOf(item);
            }
        }

        /// <summary>Checks whether every set in <paramref name="fRoot"/>'s family also belongs to <paramref name="gRoot"/>'s.</summary>
        /// <param name="manager">The manager that owns both families.</param>
        /// <param name="fRoot">Root node ID of the left family.</param>
        /// <param name="gRoot">Root node ID of the right family.</param>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static bool IsSubsetOf(ZddManager manager, int fRoot, int gRoot) =>
            Search(manager, ZddOperation.IsSubsetOf, fRoot, gRoot);

        /// <summary>Checks whether the two families share any set.</summary>
        /// <param name="manager">The manager that owns both families.</param>
        /// <param name="fRoot">Root node ID of the left family.</param>
        /// <param name="gRoot">Root node ID of the right family.</param>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static bool Overlaps(ZddManager manager, int fRoot, int gRoot) =>
            Search(manager, ZddOperation.Overlaps, fRoot, gRoot);

        /// <summary>Walks node pairs, stopping as soon as a decisive terminal condition is reached.</summary>
        /// <remarks>Overlaps stops on the first "true" sub-answer; IsSubsetOf stops on the first "false" one.</remarks>
        private static bool Search(ZddManager manager, ZddOperation op, int fRoot, int gRoot)
        {
            NodeTable nodes = manager.Table.Nodes;

            // OR-tree resolves decisively on true; AND-tree resolves decisively on false.
            bool decisive = op == ZddOperation.Overlaps;

            // Terminal-involving pairs resolve here without renting a workspace.
            if (TryResolve(nodes, work: null, op, fRoot, gRoot, out bool resolved))
            {
                return resolved;
            }

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                Remember(work, op, fRoot, gRoot);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    NodePair.Split(
                        nodes,
                        OperationKey.LeftOf(key),
                        OperationKey.RightOf(key),
                        out _,
                        out int f0,
                        out int f1,
                        out int g0,
                        out int g1);

                    if (!TryEnqueue(work, nodes, op, decisive, f0, g0) ||
                        !TryEnqueue(work, nodes, op, decisive, f1, g1))
                    {
                        return decisive;
                    }
                }

                return !decisive;
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>Pushes a sub-problem. Returns <see langword="false"/> if a decisive value was reached (signal to stop the search).</summary>
        private static bool TryEnqueue(
            OperationWorkspace work,
            NodeTable nodes,
            ZddOperation op,
            bool decisive,
            int f,
            int g)
        {
            if (TryResolve(nodes, work, op, f, g, out bool resolved))
            {
                return resolved != decisive;
            }

            Remember(work, op, f, g);
            return true;
        }

        /// <summary>Pushes an unvisited sub-problem and marks it visited.</summary>
        /// <remarks>The result table normally stores a result node ID; here only "visited" matters, so 0 is stored.</remarks>
        private static void Remember(OperationWorkspace work, ZddOperation op, int f, int g)
        {
            long key = OperationKey.Of(op, f, g);

            if (work.HasResult(key))
            {
                return;
            }

            work.SetResult(key, 0);
            work.PushVisit(key);
        }

        /// <summary>Checks whether a pair resolves without splitting; if so, <paramref name="resolved"/> holds the answer.</summary>
        /// <remarks>
        /// Handles the terminal-involving cases for both operations (∅/∅, equal families, {∅} against
        /// the other side's <see cref="HasEmptySet(NodeTable, int)"/>). Whenever this returns false,
        /// both <paramref name="f"/> and <paramref name="g"/> are non-terminal, satisfying <see cref="NodePair.Split"/>'s precondition.
        /// Pass <see langword="null"/> for <paramref name="work"/> only for the one-off pre-walk check in <see cref="Search"/>; during the walk itself, pass the workspace so trivial pairs get memoized too.
        /// </remarks>
        private static bool TryResolve(
            NodeTable nodes,
            OperationWorkspace? work,
            ZddOperation op,
            int f,
            int g,
            out bool resolved)
        {
            if (op == ZddOperation.IsSubsetOf)
            {
                if (f == NodeTable.Bottom || f == g)
                {
                    resolved = true;
                    return true;
                }

                if (g == NodeTable.Bottom || g == NodeTable.Top)
                {
                    resolved = false;
                    return true;
                }

                if (f == NodeTable.Top)
                {
                    resolved = HasEmptySet(nodes, work, op, g);
                    return true;
                }

                resolved = false;
                return false;
            }

            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                resolved = false;
                return true;
            }

            if (f == g)
            {
                resolved = true;
                return true;
            }

            if (f == NodeTable.Top || g == NodeTable.Top)
            {
                resolved = HasEmptySet(nodes, work, op, f == NodeTable.Top ? g : f);
                return true;
            }

            resolved = false;
            return false;
        }

        /// <summary>Whether the family contains the empty set: true iff the 0-branch chain from <paramref name="id"/> ends at ⊤.</summary>
        private static bool HasEmptySet(NodeTable nodes, int id)
        {
            while (!NodeTable.IsTerminal(id))
            {
                id = nodes[id].Lo;
            }

            return id == NodeTable.Top;
        }

        /// <summary><see cref="HasEmptySet(NodeTable, int)"/> with memoization in <paramref name="work"/>; falls back to the plain walk when <paramref name="work"/> is <see langword="null"/>.</summary>
        /// <remarks>
        /// Without memoization, families whose 1-branch reaches ⊤ at every level (e.g. <c>{{0},{1},...,{n-1}}</c>)
        /// re-walk an ever-shorter 0-chain at each level, costing O(n²) total. Since every node on a
        /// given 0-chain shares the same answer, one pass down (stopping at a terminal or a memoized
        /// node) followed by one pass back to write the answer keeps this at O(n). Memo keys use
        /// <c>op, ⊤, id</c> — the pair-visited keys from <see cref="Remember"/> never have ⊤ on the
        /// left (both sides are always non-terminal there), so the two key spaces never collide.
        /// </remarks>
        private static bool HasEmptySet(NodeTable nodes, OperationWorkspace? work, ZddOperation op, int id)
        {
            if (work is null)
            {
                return HasEmptySet(nodes, id);
            }

            // Walk 0-branches down to a terminal or a memoized node.
            int tail = id;
            bool hasEmptySet;

            while (true)
            {
                if (NodeTable.IsTerminal(tail))
                {
                    hasEmptySet = tail == NodeTable.Top;
                    break;
                }

                if (work.TryGetResult(OperationKey.Of(op, NodeTable.Top, tail), out int memo))
                {
                    hasEmptySet = memo == NodeTable.Top;
                    break;
                }

                tail = nodes[tail].Lo;
            }

            // Walk the same path again, writing the answer at every node visited.
            int result = hasEmptySet ? NodeTable.Top : NodeTable.Bottom;

            for (int current = id; current != tail; current = nodes[current].Lo)
            {
                work.SetResult(OperationKey.Of(op, NodeTable.Top, current), result);
            }

            return hasEmptySet;
        }
    }
}

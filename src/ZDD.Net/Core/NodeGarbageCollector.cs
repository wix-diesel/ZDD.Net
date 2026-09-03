using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Implements <see cref="ZddManager.Collect()"/>: mark &amp; sweep, node-table compaction, id
    /// remap, and rebuilding the tables that index by node id (docs/PLAN.md &#167;4.4).
    /// </summary>
    /// <remarks>
    /// Kept as its own static class, like the other operation groups (<see cref="BinaryOperations"/>,
    /// <see cref="ExtremalOperations"/>, ...), so <see cref="ZddManager.Collect()"/> itself stays a
    /// thin entry point. The mark phase walks with an explicit stack rather than recursion — a
    /// chain as deep as the variable count must not overflow the native stack (docs/PLAN.md &#167;4.5).
    /// </remarks>
    internal static class NodeGarbageCollector
    {
        /// <summary>Initial depth of the explicit mark stack; doubles on demand.</summary>
        private const int InitialStackCapacity = 64;

        /// <summary>Outcome of one <see cref="Collect"/> call, for <see cref="ZddManager"/> to fold into its statistics.</summary>
        internal readonly struct Result
        {
            public Result(long nodesBefore, long nodesAfter, TimeSpan duration)
            {
                NodesBefore = nodesBefore;
                NodesAfter = nodesAfter;
                Duration = duration;
            }

            /// <summary>Live (non-terminal) node count immediately before this collection.</summary>
            public long NodesBefore { get; }

            /// <summary>Live (non-terminal) node count immediately after this collection.</summary>
            public long NodesAfter { get; }

            /// <summary>Nodes reclaimed by this collection.</summary>
            public long NodesRemoved => NodesBefore - NodesAfter;

            /// <summary>Wall-clock time this collection took.</summary>
            public TimeSpan Duration { get; }
        }

        /// <summary>
        /// Runs one collection on <paramref name="manager"/>: marks everything reachable from its
        /// <see cref="ZddManager.RootSet"/>, compacts the node table down to just that, rebuilds the
        /// unique table over the result, clears the operation cache, and remaps
        /// <see cref="ZddManager.RootSet"/> and the cached power-set root to their new ids.
        /// </summary>
        /// <remarks>
        /// Does not bump <see cref="ZddManager.Generation"/> itself; the caller
        /// (<see cref="ZddManager.Collect()"/>) does that once this returns, since a handle read
        /// from <see cref="ZddManager.RootSet"/> mid-collection would otherwise observe a stale
        /// generation for an instant.
        /// </remarks>
        internal static Result Collect(ZddManager manager)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            UniqueTable table = manager.Table;
            NodeTable nodes = table.Nodes;

            long before = nodes.Count;

            bool[] live = Mark(nodes, manager.RootSet.Ids);
            int[] map = nodes.Compact(live);

            table.RebuildAfterCollection();
            manager.Cache.Clear();

            manager.RootSet.Remap(map);
            manager.RemapPowerSetRoot(map);

            long after = nodes.Count;

            stopwatch.Stop();
            return new Result(before, after, stopwatch.Elapsed);
        }

        /// <summary>
        /// Marks every node reachable from <paramref name="rootIds"/>, iteratively (explicit stack,
        /// no recursion).
        /// </summary>
        /// <param name="nodes">The node table to walk.</param>
        /// <param name="rootIds">Mark roots (typically <see cref="ZddRootSet.Ids"/>); terminals among them are ignored.</param>
        /// <returns>Liveness by id, indexed 0 .. <c>nodes.NextId</c> - 1.</returns>
        private static bool[] Mark(NodeTable nodes, List<int> rootIds)
        {
            bool[] live = new bool[nodes.NextId];
            int[] stack = new int[InitialStackCapacity];
            int top = 0;

            foreach (int rootId in rootIds)
            {
                if (NodeTable.IsTerminal(rootId) || live[rootId])
                {
                    continue;
                }

                live[rootId] = true;
                Push(ref stack, ref top, rootId);
            }

            while (top > 0)
            {
                ref ZddNode node = ref nodes[stack[--top]];
                int lo = node.Lo;
                int hi = node.Hi;

                if (!NodeTable.IsTerminal(lo) && !live[lo])
                {
                    live[lo] = true;
                    Push(ref stack, ref top, lo);
                }

                if (!NodeTable.IsTerminal(hi) && !live[hi])
                {
                    live[hi] = true;
                    Push(ref stack, ref top, hi);
                }
            }

            return live;
        }

        private static void Push(ref int[] stack, ref int top, int id)
        {
            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = id;
        }
    }
}

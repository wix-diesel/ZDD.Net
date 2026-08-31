using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Item-taking unary operations (<see cref="ZddOperation.Change"/> /
    /// <see cref="ZddOperation.OnSet"/> / <see cref="ZddOperation.OffSet"/>).
    /// </summary>
    /// <remarks>
    /// This type is the template for the iterative traversal used by the other operation types
    /// (docs/PLAN.md §4.5): only the base case and combine step change. The traversal is
    /// iterative (explicit stack) to avoid stack overflow on deep diagrams. All three ops share
    /// one loop because they only differ in a few lines of base-case logic.
    /// </remarks>
    internal static class UnaryOperations
    {
        /// <summary>Applies a unary operation to the family rooted at <paramref name="rootId"/> and returns the resulting root node id.</summary>
        /// <param name="manager">Manager owning the family.</param>
        /// <param name="op">One of the item-taking unary operations.</param>
        /// <param name="rootId">Root node id of the input family.</param>
        /// <param name="item">Item index the operation targets.</param>
        /// <returns>Root node id of the resulting family.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> is out of range for <paramref name="manager"/>.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> is disposed.</exception>
        public static int Apply(ZddManager manager, ZddOperation op, int rootId, int item)
        {
            Debug.Assert(
                op is ZddOperation.Change or ZddOperation.OnSet or ZddOperation.OffSet,
                $"'{op}' is not one of the item-taking unary operations.");

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            // Also validates item's range; level stays fixed for the rest of the operation.
            int level = manager.LevelOf(item);

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                work.PushVisit(rootId);

                while (work.TryPop(out long entry))
                {
                    int id = (int)OperationWorkspace.KeyOf(entry);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // Children are already computed. Read everything before GetNode, since it
                        // may grow the node table and invalidate an existing ref.
                        int nodeLevel;
                        int childLo;
                        int childHi;
                        {
                            ref ZddNode node = ref nodes[id];
                            nodeLevel = node.Level;
                            childLo = node.Lo;
                            childHi = node.Hi;
                        }

                        work.TryGetResult(childLo, out int loResult);
                        work.TryGetResult(childHi, out int hiResult);

                        int combined = table.GetNode(nodeLevel, loResult, hiResult);
                        work.SetResult(id, combined);
                        cache.PutUnary(op, id, item, combined);
                        continue;
                    }

                    if (work.HasResult(id))
                    {
                        continue;
                    }

                    // Base case: reached the item's level (or below).
                    int currentLevel = NodeTable.IsTerminal(id) ? 0 : nodes[id].Level;
                    if (currentLevel <= level)
                    {
                        work.SetResult(id, BaseCase(table, nodes, op, id, currentLevel, level));
                        continue;
                    }

                    // Checked after the base case since the base case is cheaper than a lookup.
                    if (cache.TryGetUnary(op, id, item, out int cached))
                    {
                        work.SetResult(id, cached);
                        continue;
                    }

                    // Above the item's level: pass through, applying the op to both children.
                    int lo;
                    int hi;
                    {
                        ref ZddNode node = ref nodes[id];
                        lo = node.Lo;
                        hi = node.Hi;
                    }

                    work.PushCombine(id);

                    if (!work.HasResult(lo))
                    {
                        work.PushVisit(lo);
                    }

                    if (!work.HasResult(hi))
                    {
                        work.PushVisit(hi);
                    }
                }

                work.TryGetResult(rootId, out int result);
                return result;
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>Answer for a node at or below the item's level. The only part that differs per operation.</summary>
        /// <param name="table">Unique table used to create nodes.</param>
        /// <param name="nodes">Node table.</param>
        /// <param name="op">Operation kind.</param>
        /// <param name="id">Target node id.</param>
        /// <param name="currentLevel">Level of <paramref name="id"/> (0 if terminal).</param>
        /// <param name="level">Level of the target item.</param>
        private static int BaseCase(
            UniqueTable table,
            NodeTable nodes,
            ZddOperation op,
            int id,
            int currentLevel,
            int level)
        {
            Debug.Assert(currentLevel <= level, "BaseCase is only reached at or below the item's level.");

            if (currentLevel < level)
            {
                // This family never mentions the item: no set contains it.
                return op switch
                {
                    ZddOperation.Change => table.GetNode(level, NodeTable.Bottom, id),
                    ZddOperation.OnSet => NodeTable.Bottom,
                    ZddOperation.OffSet => id,
                    _ => ThrowUnsupported(op),
                };
            }

            // currentLevel == level: this is the item's own branch node.
            int lo;
            int hi;
            {
                ref ZddNode node = ref nodes[id];
                lo = node.Lo;
                hi = node.Hi;
            }

            return op switch
            {
                ZddOperation.Change => table.GetNode(level, hi, lo),
                ZddOperation.OnSet => hi,
                ZddOperation.OffSet => lo,
                _ => ThrowUnsupported(op),
            };
        }

        [DoesNotReturn]
        private static int ThrowUnsupported(ZddOperation op) =>
            throw new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the item-taking unary operations (Change / OnSet / OffSet).");
    }
}

using System;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Implements the order-preserving fast path of <see cref="Zdd.MapItems"/> (M6-4, issue #139):
    /// a bottom-up, single-pass rebuild that relabels every node's branch item via <c>itemMap</c>.
    /// </summary>
    /// <remarks>
    /// Correctness relies entirely on the caller (<see cref="ZddManager.MapItems"/>) having already
    /// confirmed that <c>itemMap</c> is strictly increasing on the family's support: that is exactly
    /// what keeps <c>parent level &gt; child level</c> true after relabeling (level = VariableCount -
    /// item), so every <see cref="UniqueTable.GetNode"/> call below stays within the ZDD invariant
    /// without this code needing to re-check it node by node. Iterative (explicit stack via
    /// <see cref="OperationWorkspace"/>), like every other operation (docs/PLAN.md &#167;4.5), so the
    /// traversal depth never depends on the native call stack. General (non-monotonic) permutation
    /// and cross-manager transfer are out of scope here; see M6-5.
    /// </remarks>
    internal static class MapItemsOperation
    {
        /// <summary>Rebuilds the family rooted at <paramref name="rootId"/>, relabeling every branch item via <paramref name="itemMap"/>.</summary>
        /// <param name="manager">Manager owning the family (source and destination — this stays within one manager).</param>
        /// <param name="rootId">Root node id of the input family.</param>
        /// <param name="itemMap">
        /// Old-item-to-new-item map, already validated by the caller: total, injective, and strictly
        /// increasing on <paramref name="rootId"/>'s support.
        /// </param>
        /// <returns>Root node id of the resulting family.</returns>
        public static int Apply(ZddManager manager, int rootId, ReadOnlySpan<int> itemMap)
        {
            if (NodeTable.IsTerminal(rootId))
            {
                return rootId;
            }

            UniqueTable table = manager.Table;
            NodeTable nodes = table.Nodes;
            int variableCount = manager.VariableCount;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                work.PushVisit(rootId);

                while (work.TryPop(out long entry))
                {
                    int id = (int)OperationWorkspace.KeyOf(entry);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // Children are already computed. Read node fields up front, since GetNode
                        // below can grow the node table and invalidate an existing ref.
                        int oldLevel;
                        int lo;
                        int hi;
                        {
                            ref ZddNode node = ref nodes[id];
                            oldLevel = node.Level;
                            lo = node.Lo;
                            hi = node.Hi;
                        }

                        work.TryGetResult(lo, out int loResult);
                        work.TryGetResult(hi, out int hiResult);

                        int newItem = itemMap[variableCount - oldLevel];
                        int newLevel = variableCount - newItem;

                        int combined = table.GetNode(newLevel, loResult, hiResult);
                        work.SetResult(id, combined);
                        continue;
                    }

                    // Already solved (shared node reached via another parent).
                    if (work.HasResult(id))
                    {
                        continue;
                    }

                    // Base case: terminals map to themselves.
                    if (NodeTable.IsTerminal(id))
                    {
                        work.SetResult(id, id);
                        continue;
                    }

                    int childLo;
                    int childHi;
                    {
                        ref ZddNode node = ref nodes[id];
                        childLo = node.Lo;
                        childHi = node.Hi;
                    }

                    work.PushCombine(id);

                    if (!work.HasResult(childLo))
                    {
                        work.PushVisit(childLo);
                    }

                    if (!work.HasResult(childHi))
                    {
                        work.PushVisit(childHi);
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
    }
}

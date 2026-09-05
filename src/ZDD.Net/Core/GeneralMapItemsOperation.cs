using System;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Implements the general (non-order-preserving) path of <see cref="Zdd.MapItemsTo"/> (M6-5,
    /// issue #140): rebuilds a family node by node when <c>itemMap</c> is not strictly increasing
    /// on the family's support, so <see cref="MapItemsOperation"/>'s bottom-up rebuild would
    /// otherwise put a child above its parent's level.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses the ZDD recursive definition directly: <c>f = f0 &#8746; (f1 &#215; {v})</c>, where
    /// <c>v</c> is the node's item, <c>f0</c> is the 0-edge family and <c>f1</c> is the 1-edge
    /// family (already stripped of <c>v</c>). Substituting <c>&#963;</c> for every item gives
    /// <c>map(f) = map(f0) &#8746; Change(map(f1), &#963;(v))</c> &#8212; computed here bottom-up,
    /// with one <see cref="ZddOperation.Change"/> and one <see cref="ZddOperation.Union"/> call per
    /// source node, both run on the target manager so the general path doubles as the
    /// cross-manager transfer path.
    /// </para>
    /// <para>
    /// <b>Correctness</b>: every item in <c>f1</c>'s subtree is strictly greater than <c>v</c> (ZDD
    /// nodes only branch on items below their own). Since <c>&#963;</c> is injective, <c>&#963;(v)</c>
    /// cannot equal <c>&#963;(item)</c> for any such item, so <c>&#963;(v)</c> never appears in
    /// <c>map(f1)</c>'s support &#8212; <see cref="ZddOperation.Change"/> against it is therefore
    /// always an addition, never an accidental flip, regardless of whether <c>&#963;</c> preserves
    /// order.
    /// </para>
    /// <para>
    /// <b>Cost</b>: node count &#215; (<see cref="ZddOperation.Union"/> + <see cref="ZddOperation.Change"/>)
    /// calls, each up to O(node count) &#8212; not linear like the order-preserving fast path, but
    /// not exponential either. Iterative (explicit stack via <see cref="OperationWorkspace"/>), like
    /// every other operation (docs/PLAN.md &#167;4.5), so the traversal depth never depends on the
    /// native call stack.
    /// </para>
    /// </remarks>
    internal static class GeneralMapItemsOperation
    {
        /// <summary>Rebuilds the family rooted at <paramref name="rootId"/> in <paramref name="target"/>, relabeling every branch item via <paramref name="itemMap"/>.</summary>
        /// <param name="source">Manager owning the input family.</param>
        /// <param name="target">Manager the rebuilt family is created in (may be the same as <paramref name="source"/>).</param>
        /// <param name="rootId">Root node id of the input family, in <paramref name="source"/>.</param>
        /// <param name="itemMap">
        /// Old-item-to-new-item map, already validated by the caller: total and injective into
        /// <paramref name="target"/>'s variables. Need not be order-preserving.
        /// </param>
        /// <returns>Root node id of the resulting family, in <paramref name="target"/>.</returns>
        public static int Apply(ZddManager source, ZddManager target, int rootId, ReadOnlySpan<int> itemMap)
        {
            if (NodeTable.IsTerminal(rootId))
            {
                return rootId;
            }

            NodeTable sourceNodes = source.Table.Nodes;
            int sourceVariableCount = source.VariableCount;

            OperationWorkspace work = source.RentWorkspace();
            try
            {
                work.PushVisit(rootId);

                while (work.TryPop(out long entry))
                {
                    int id = (int)OperationWorkspace.KeyOf(entry);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // Children are already computed (as results in target).
                        int oldLevel;
                        int lo;
                        int hi;
                        {
                            ref ZddNode node = ref sourceNodes[id];
                            oldLevel = node.Level;
                            lo = node.Lo;
                            hi = node.Hi;
                        }

                        work.TryGetResult(lo, out int loResult);
                        work.TryGetResult(hi, out int hiResult);

                        int newItem = itemMap[sourceVariableCount - oldLevel];

                        // map(f) = map(f0) ∪ Change(map(f1), σ(v))
                        int changedHi = UnaryOperations.Apply(target, ZddOperation.Change, hiResult, newItem);
                        int combined = BinaryOperations.Apply(target, ZddOperation.Union, loResult, changedHi);

                        work.SetResult(id, combined);
                        continue;
                    }

                    // Already solved (shared node reached via another parent).
                    if (work.HasResult(id))
                    {
                        continue;
                    }

                    // Base case: terminals map to themselves (shared id across every manager).
                    if (NodeTable.IsTerminal(id))
                    {
                        work.SetResult(id, id);
                        continue;
                    }

                    int childLo;
                    int childHi;
                    {
                        ref ZddNode node = ref sourceNodes[id];
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
                source.ReturnWorkspace(work);
            }
        }
    }
}

using System;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Implements the item-less unary operations (<see cref="ZddOperation.Maximal"/> / <see cref="ZddOperation.Minimal"/> /
    /// <see cref="ZddOperation.HittingSets"/> / <see cref="ZddOperation.Complement"/>).
    /// </summary>
    /// <remarks>
    /// Maximal/Minimal walk a single node (key = one node ID, like <see cref="UnaryOperations"/>).
    /// HittingSets' answer depends on how many free levels remain above the current node, so its
    /// key is a <c>(node, level)</c> pair. Complement is delegated entirely to
    /// <see cref="BinaryOperations"/>'s difference against the power set. All nested calls (sieving,
    /// union, difference) are iterative and rent their own workspace, so recursion depth never
    /// scales with family size — avoiding an uncatchable <c>StackOverflowException</c>.
    /// </remarks>
    internal static class ExtremalOperations
    {
        /// <summary>
        /// Applies an item-less unary operation to a family and returns the result's root node ID.
        /// </summary>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="op"><see cref="ZddOperation.Maximal"/> / <see cref="ZddOperation.Minimal"/> / <see cref="ZddOperation.HittingSets"/> / <see cref="ZddOperation.Complement"/>.</param>
        /// <param name="rootId">Root node ID of the input family.</param>
        /// <returns>Root node ID of the resulting family.</returns>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static int Apply(ZddManager manager, ZddOperation op, int rootId) =>
            op switch
            {
                ZddOperation.Maximal or ZddOperation.Minimal => Extremal(manager, op, rootId),
                ZddOperation.HittingSets => HittingSets(manager, rootId),
                ZddOperation.Complement => Complement(manager, rootId),
                _ => throw Unsupported(op),
            };

        // ---- Maximal / Minimal ----

        /// <summary>Keeps only the sets that are maximal (or minimal) under containment.</summary>
        /// <remarks>
        /// At level <c>v</c>, <c>f = f₀ ∪ v·f₁</c>. Containment between the branches only ever runs
        /// one direction (a set containing <c>v</c> can't be contained in one that doesn't), so only
        /// one side ever needs sieving: for Minimal, drop <c>f₁</c> elements that are supersets of an
        /// <c>f₀</c> minimal element; for Maximal, drop <c>f₀</c> elements that are subsets of an
        /// <c>f₁</c> maximal element. It's enough to sieve against the already-reduced (extremal)
        /// side rather than the raw child, since containing any element implies containing an extremal
        /// one. The sieve itself is <see cref="ContainmentOperations"/>'s NonSupersetsOf/NonSubsetsOf.
        /// </remarks>
        private static int Extremal(ZddManager manager, ZddOperation op, int rootId)
        {
            // A terminal is its own answer (∅ has no elements; {∅}'s only element is trivially extremal).
            if (NodeTable.IsTerminal(rootId))
            {
                return rootId;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            bool keepsMinimal = op == ZddOperation.Minimal;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                work.PushVisit(rootId);

                while (work.TryPop(out long entry))
                {
                    int id = (int)OperationWorkspace.KeyOf(entry);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // Children are already computed. Read node fields up front: Filter/GetNode
                        // below can grow the node table and invalidate a `ref` into it.
                        int level;
                        int nodeLo;
                        int nodeHi;
                        {
                            ref ZddNode node = ref nodes[id];
                            level = node.Level;
                            nodeLo = node.Lo;
                            nodeHi = node.Hi;
                        }

                        work.TryGetResult(nodeLo, out int lo);
                        work.TryGetResult(nodeHi, out int hi);

                        if (keepsMinimal)
                        {
                            hi = Filter(manager, ZddOperation.NonSupersetsOf, hi, lo);
                        }
                        else
                        {
                            lo = Filter(manager, ZddOperation.NonSubsetsOf, lo, hi);
                        }

                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(id, combined);
                        cache.PutUnary(op, id, 0, combined);
                        continue;
                    }

                    // 1) already solved by another parent
                    if (work.HasResult(id))
                    {
                        continue;
                    }

                    // 2) base case
                    if (NodeTable.IsTerminal(id))
                    {
                        work.SetResult(id, id);
                        continue;
                    }

                    // 3) operation cache
                    if (cache.TryGetUnary(op, id, 0, out int cached))
                    {
                        work.SetResult(id, cached);
                        continue;
                    }

                    // 4) descend one level
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

        // ---- Hitting sets ----

        /// <summary>Computes <c>{ a ⊆ U : ∀ b ∈ f, a ∩ b ≠ ∅ }</c> (the transversal hypergraph / blocking sets).</summary>
        /// <remarks>
        /// <c>U</c> is all of <see cref="ZddManager.VariableCount"/>, not just <c>f</c>'s support.
        /// The answer depends on how many free item-levels remain above the current node, so the
        /// walk key is <c>(node, level)</c> rather than just a node ID (see <see cref="OperationKey"/>
        /// packing: node in the left half, level in the right). Items above <c>f</c>'s top level are
        /// free either way, so both branches of those nodes point at the same answer and the result
        /// naturally balloons by a power-of-two factor — this is also why <c>f = ∅</c> yields <c>2^U</c>.
        /// The result can be exponentially larger than the input; chain <c>.Minimal()</c> if only
        /// minimal hitting sets are needed.
        /// </remarks>
        private static int HittingSets(ZddManager manager, int rootId)
        {
            // {∅} can't intersect anything, so the answer is empty; resolve before renting a workspace.
            if (rootId == NodeTable.Top)
            {
                return NodeTable.Bottom;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(ZddOperation.HittingSets, rootId, manager.VariableCount);
                work.PushVisit(rootKey);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    int f = OperationKey.LeftOf(key);
                    int level = OperationKey.RightOf(key);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        int lo;
                        int hi;

                        if (level > LevelOf(nodes, f))
                        {
                            // item doesn't appear in f: free either way, both branches agree.
                            lo = HittingOf(work, f, level - 1);
                            hi = lo;
                        }
                        else
                        {
                            int f0;
                            int f1;
                            {
                                ref ZddNode node = ref nodes[f];
                                f0 = node.Lo;
                                f1 = node.Hi;
                            }

                            // Excluding item requires hitting f with item stripped from every set.
                            lo = HittingOf(work, Shadow(manager, f0, f1), level - 1);
                            hi = HittingOf(work, f0, level - 1);
                        }

                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(key, combined);
                        cache.PutUnary(ZddOperation.HittingSets, f, level, combined);
                        continue;
                    }

                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    if (TryResolveHitting(f, level, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    if (cache.TryGetUnary(ZddOperation.HittingSets, f, level, out int cached))
                    {
                        work.SetResult(key, cached);
                        continue;
                    }

                    work.PushCombine(key);

                    if (level > LevelOf(nodes, f))
                    {
                        PushHitting(work, f, level - 1);
                    }
                    else
                    {
                        int childLo;
                        int childHi;
                        {
                            ref ZddNode node = ref nodes[f];
                            childLo = node.Lo;
                            childHi = node.Hi;
                        }

                        PushHitting(work, Shadow(manager, childLo, childHi), level - 1);
                        PushHitting(work, childLo, level - 1);
                    }
                }

                work.TryGetResult(rootKey, out int result);
                return result;
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>Resolves hitting-set cases involving a terminal.</summary>
        /// <returns><see langword="true"/> if the answer was determined.</returns>
        /// <remarks><c>∅</c> with levels still free is not resolved here; it's built up level by level into the power set.</remarks>
        private static bool TryResolveHitting(int f, int level, out int result)
        {
            if (f == NodeTable.Top)
            {
                result = NodeTable.Bottom;
                return true;
            }

            if (f == NodeTable.Bottom && level == 0)
            {
                result = NodeTable.Top;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>Pushes the sub-problem <c>(f, level)</c>. Pairs that resolve trivially are not pushed.</summary>
        /// <remarks>Must use the same trivial-case test as the combine step.</remarks>
        private static void PushHitting(OperationWorkspace work, int f, int level)
        {
            if (TryResolveHitting(f, level, out _))
            {
                return;
            }

            long key = HittingKey(f, level);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>Reads the computed sub-problem <c>(f, level)</c>. Pairs with <see cref="PushHitting"/>.</summary>
        private static int HittingOf(OperationWorkspace work, int f, int level)
        {
            if (TryResolveHitting(f, level, out int direct))
            {
                return direct;
            }

            work.TryGetResult(HittingKey(f, level), out int result);
            return result;
        }

        /// <summary>Key for sub-problem <c>(f, level)</c>.</summary>
        private static long HittingKey(int f, int level) =>
            OperationKey.Of(ZddOperation.HittingSets, f, level);

        /// <summary>The family with the branching item removed from every set (<c>f₀ ∪ f₁</c>).</summary>
        private static int Shadow(ZddManager manager, int f0, int f1) =>
            BinaryOperations.Apply(manager, ZddOperation.Union, f0, f1);

        // ---- Complement ----

        /// <summary>Computes the complement <c>2^U ∖ f</c>, where <c>U</c> is all of the manager's variables.</summary>
        /// <remarks>Result is cached as a unary op so repeated complements skip rebuilding the power set.</remarks>
        private static int Complement(ZddManager manager, int rootId)
        {
            OperationCache cache = manager.Cache;

            if (cache.TryGetUnary(ZddOperation.Complement, rootId, 0, out int cached))
            {
                return cached;
            }

            int result = BinaryOperations.Apply(
                manager,
                ZddOperation.Difference,
                manager.PowerSetRoot(),
                rootId);

            cache.PutUnary(ZddOperation.Complement, rootId, 0, result);
            return result;
        }

        // ---- Shared helpers ----

        /// <summary>The node's level. Terminals are level 0 (deeper than any item).</summary>
        private static int LevelOf(NodeTable nodes, int id) =>
            NodeTable.IsTerminal(id) ? 0 : nodes[id].Level;

        /// <summary>Applies the containment sieve used by the Maximal/Minimal combine step, using its own rented workspace.</summary>
        private static int Filter(ZddManager manager, ZddOperation op, int f, int g) =>
            ContainmentOperations.Apply(manager, op, f, g);

        private static ArgumentOutOfRangeException Unsupported(ZddOperation op) =>
            new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the item-less unary operations " +
                "(Maximal / Minimal / HittingSets / Complement).");
    }
}

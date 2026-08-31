using System;
using System.Diagnostics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Implements the polynomial-style family operations (<see cref="ZddOperation.Product"/> /
    /// <see cref="ZddOperation.Quotient"/> / <see cref="ZddOperation.Remainder"/>).
    /// </summary>
    /// <remarks>
    /// Unlike the four set operations in <see cref="BinaryOperations"/>, a combine step here can
    /// require another full operation: a product's 1-branch is a union of three sub-products, and
    /// a quotient's combine needs an intersection. Those nested calls rent their own workspace
    /// (<see cref="ZddManager.RentWorkspace"/>), so they never collide with this operation's stack.
    /// The walk is iterative (no recursion) because ZDD depth equals variable count, and a naive
    /// recursive walk over large families would overflow the native stack uncatchably.
    /// Remainder is built directly from its definition (<c>f % g = f ∖ (g * (f / g))</c>) rather
    /// than a bespoke traversal, since it only reuses Quotient and Product anyway.
    /// </remarks>
    internal static class FamilyAlgebraOperations
    {
        /// <summary>
        /// Applies product, quotient, or remainder to two families and returns the result's root node ID.
        /// </summary>
        /// <param name="manager">The manager that owns both families.</param>
        /// <param name="op"><see cref="ZddOperation.Product"/>, <see cref="ZddOperation.Quotient"/>, or <see cref="ZddOperation.Remainder"/>.</param>
        /// <param name="fRoot">Root node ID of the left operand.</param>
        /// <param name="gRoot">Root node ID of the right operand.</param>
        /// <returns>Root node ID of the resulting family.</returns>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static int Apply(ZddManager manager, ZddOperation op, int fRoot, int gRoot) =>
            op switch
            {
                ZddOperation.Product => Product(manager, fRoot, gRoot),
                ZddOperation.Quotient => Quotient(manager, fRoot, gRoot),
                ZddOperation.Remainder => Remainder(manager, fRoot, gRoot),
                _ => throw Unsupported(op),
            };

        // ---- Product ----

        /// <summary>Computes the product <c>f * g = { a ∪ b : a ∈ f, b ∈ g }</c>.</summary>
        private static int Product(ZddManager manager, int fRoot, int gRoot)
        {
            // Terminal cases are resolved here, before renting a workspace.
            if (TryResolveProduct(fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(ZddOperation.Product, fRoot, gRoot);
                work.PushVisit(rootKey);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    int f = OperationKey.LeftOf(key);
                    int g = OperationKey.RightOf(key);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // Children are already computed (pushed LIFO right after this combine entry).
                        NodePair.Split(nodes, f, g, out int level, out int f0, out int f1, out int g0, out int g1);

                        int lo = ProductOf(work, f0, g0);

                        // The 1-branch is the union of three sub-products.
                        int hi = ProductOf(work, f1, g1);
                        hi = Combine(manager, ZddOperation.Union, hi, ProductOf(work, f1, g0));
                        hi = Combine(manager, ZddOperation.Union, hi, ProductOf(work, f0, g1));

                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(key, combined);
                        cache.PutBinary(ZddOperation.Product, f, g, combined);
                        continue;
                    }

                    // 1) already solved by another parent
                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    // 2) base case
                    if (TryResolveProduct(f, g, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    // 3) operation cache
                    if (cache.TryGetBinary(ZddOperation.Product, f, g, out int cached))
                    {
                        work.SetResult(key, cached);
                        continue;
                    }

                    // 4) descend one level
                    NodePair.Split(
                        nodes,
                        f,
                        g,
                        out _,
                        out int childF0,
                        out int childF1,
                        out int childG0,
                        out int childG1);

                    work.PushCombine(key);
                    PushProduct(work, childF0, childG0);
                    PushProduct(work, childF1, childG1);
                    PushProduct(work, childF1, childG0);
                    PushProduct(work, childF0, childG1);
                }

                work.TryGetResult(rootKey, out int result);
                return result;
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>Resolves a product where a terminal is involved: <c>∅ * g = ∅</c>, <c>{∅} * g = g</c>.</summary>
        /// <returns><see langword="true"/> if the answer was determined.</returns>
        /// <remarks><c>f == g</c> is not a shortcut: <c>f * f</c> is not <c>f</c> in general.</remarks>
        private static bool TryResolveProduct(int f, int g, out int result)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                result = NodeTable.Bottom;
                return true;
            }

            if (f == NodeTable.Top)
            {
                result = g;
                return true;
            }

            if (g == NodeTable.Top)
            {
                result = f;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>Pushes the sub-product <c>(f, g)</c>. Pairs involving ∅ are skipped entirely.</summary>
        /// <remarks>Must use the same trivial-case test as <see cref="ProductOf"/>.</remarks>
        private static void PushProduct(OperationWorkspace work, int f, int g)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                return;
            }

            long key = OperationKey.Of(ZddOperation.Product, f, g);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>Reads the computed sub-product <c>(f, g)</c>. Pairs with <see cref="PushProduct"/>.</summary>
        private static int ProductOf(OperationWorkspace work, int f, int g)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                return NodeTable.Bottom;
            }

            work.TryGetResult(OperationKey.Of(ZddOperation.Product, f, g), out int result);
            return result;
        }

        // ---- Quotient ----

        /// <summary>Computes the quotient <c>f / g = { a : ∀ b ∈ g, a ∩ b = ∅ and a ∪ b ∈ f }</c>.</summary>
        /// <remarks>
        /// <c>g == ∅</c> makes the condition vacuously true (answer is the full power set), so it is
        /// handled before the walk starts; no sub-problem in the walk ever has <c>g == ∅</c>.
        /// </remarks>
        private static int Quotient(ZddManager manager, int fRoot, int gRoot)
        {
            if (gRoot == NodeTable.Bottom)
            {
                return manager.PowerSetRoot();
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            if (TryResolveQuotient(nodes, fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(ZddOperation.Quotient, fRoot, gRoot);
                work.PushVisit(rootKey);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    int f = OperationKey.LeftOf(key);
                    int g = OperationKey.RightOf(key);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        NodePair.Split(nodes, f, g, out int level, out int f0, out int f1, out int g0, out int g1);

                        int combined;
                        if (IsAboveDivisor(nodes, f, g))
                        {
                            // item does not appear in g, so it may remain in the quotient.
                            combined = table.GetNode(
                                level,
                                QuotientOf(work, f0, g),
                                QuotientOf(work, f1, g));
                        }
                        else
                        {
                            // g's top item; quotient elements never include it.
                            combined = QuotientOf(work, f1, g1);

                            if (g0 != NodeTable.Bottom)
                            {
                                combined = Combine(
                                    manager,
                                    ZddOperation.Intersect,
                                    combined,
                                    QuotientOf(work, f0, g0));
                            }
                        }

                        work.SetResult(key, combined);
                        cache.PutBinary(ZddOperation.Quotient, f, g, combined);
                        continue;
                    }

                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    if (TryResolveQuotient(nodes, f, g, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    if (cache.TryGetBinary(ZddOperation.Quotient, f, g, out int cached))
                    {
                        work.SetResult(key, cached);
                        continue;
                    }

                    NodePair.Split(
                        nodes,
                        f,
                        g,
                        out _,
                        out int childF0,
                        out int childF1,
                        out int childG0,
                        out int childG1);

                    work.PushCombine(key);

                    if (IsAboveDivisor(nodes, f, g))
                    {
                        PushQuotient(work, childF0, g);
                        PushQuotient(work, childF1, g);
                    }
                    else
                    {
                        PushQuotient(work, childF1, childG1);

                        if (childG0 != NodeTable.Bottom)
                        {
                            PushQuotient(work, childF0, childG0);
                        }
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

        /// <summary>Resolves quotient cases involving a terminal, or where <paramref name="f"/> is too shallow. <c>g == ∅</c> never reaches this.</summary>
        /// <returns><see langword="true"/> if the answer was determined.</returns>
        private static bool TryResolveQuotient(NodeTable nodes, int f, int g, out int result)
        {
            Debug.Assert(g != NodeTable.Bottom, "Division by the empty family is settled before the walk starts.");

            if (g == NodeTable.Top)
            {
                result = f;
                return true;
            }

            if (f == NodeTable.Bottom)
            {
                result = NodeTable.Bottom;
                return true;
            }

            if (f == g)
            {
                // f / f = {∅}: any nonempty candidate would need f to contain an ever-larger set.
                result = NodeTable.Top;
                return true;
            }

            // If f's top item is below g's, f has no set containing it, so no a satisfies a ∪ b ∈ f.
            int fLevel = NodeTable.IsTerminal(f) ? 0 : nodes[f].Level;
            if (fLevel < nodes[g].Level)
            {
                result = NodeTable.Bottom;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>Whether <paramref name="f"/>'s top item is above (root-side of) <paramref name="g"/>'s.</summary>
        /// <remarks>Both arguments are known non-terminal here (terminals are filtered out by the base case). Used consistently at push time and combine time.</remarks>
        private static bool IsAboveDivisor(NodeTable nodes, int f, int g) => nodes[f].Level > nodes[g].Level;

        /// <summary>Pushes the sub-quotient <c>(f, g)</c>.</summary>
        private static void PushQuotient(OperationWorkspace work, int f, int g)
        {
            long key = OperationKey.Of(ZddOperation.Quotient, f, g);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>Reads the computed sub-quotient <c>(f, g)</c>. Pairs with <see cref="PushQuotient"/>.</summary>
        private static int QuotientOf(OperationWorkspace work, int f, int g)
        {
            work.TryGetResult(OperationKey.Of(ZddOperation.Quotient, f, g), out int result);
            return result;
        }

        // ---- Remainder ----

        /// <summary>Computes the remainder <c>f % g = f ∖ (g * (f / g))</c>.</summary>
        /// <remarks>Only the root result is cached under <see cref="ZddOperation.Remainder"/>.</remarks>
        private static int Remainder(ZddManager manager, int fRoot, int gRoot)
        {
            if (gRoot == NodeTable.Top)
            {
                // f % {∅} = f ∖ ({∅} * f) = ∅.
                return NodeTable.Bottom;
            }

            if (gRoot == NodeTable.Bottom || fRoot == NodeTable.Bottom)
            {
                // f % ∅ = f; and if f = ∅ there's nothing to subtract from either way.
                return fRoot;
            }

            OperationCache cache = manager.Cache;
            if (cache.TryGetBinary(ZddOperation.Remainder, fRoot, gRoot, out int cached))
            {
                return cached;
            }

            int quotient = Quotient(manager, fRoot, gRoot);
            int divisible = Product(manager, gRoot, quotient);
            int result = BinaryOperations.Apply(manager, ZddOperation.Difference, fRoot, divisible);

            cache.PutBinary(ZddOperation.Remainder, fRoot, gRoot, result);
            return result;
        }

        // ---- Shared helpers ----

        /// <summary>Applies another binary operation (union / intersect) as part of a combine step, using its own rented workspace.</summary>
        private static int Combine(ZddManager manager, ZddOperation op, int f, int g) =>
            BinaryOperations.Apply(manager, op, f, g);

        private static ArgumentOutOfRangeException Unsupported(ZddOperation op) =>
            new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the family algebra operations (Product / Quotient / Remainder).");
    }
}

using System;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Filters a family by containment against another family
    /// (<see cref="ZddOperation.Meet"/> / <see cref="ZddOperation.SupersetsOf"/> /
    /// <see cref="ZddOperation.SubsetsOf"/> / <see cref="ZddOperation.NonSubsetsOf"/> /
    /// <see cref="ZddOperation.NonSupersetsOf"/>).
    /// </summary>
    /// <remarks>
    /// The four filters differ only in search direction (<see cref="SeeksSubsetsInG"/>) and
    /// whether matches are kept or dropped (<see cref="KeepsMatches"/>); merging two answers
    /// uses <see cref="ZddOperation.Union"/> or <see cref="ZddOperation.Intersect"/>
    /// depending on that. <see cref="ZddOperation.Meet"/> instead builds
    /// <c>{ a ∩ b : a ∈ f, b ∈ g }</c>, so it collects 3 subproblems into the 0-edge.
    /// The traversal is iterative (explicit stack) to avoid stack overflow on deep diagrams.
    /// </remarks>
    internal static class ContainmentOperations
    {
        /// <summary>Applies a containment operation to two families and returns the resulting root node id.</summary>
        /// <param name="manager">Manager owning both families.</param>
        /// <param name="op">One of the containment operations.</param>
        /// <param name="fRoot">Root node id of the family being filtered (left operand).</param>
        /// <param name="gRoot">Root node id of the other family (right operand).</param>
        /// <returns>Root node id of the resulting family.</returns>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> is disposed.</exception>
        public static int Apply(ZddManager manager, ZddOperation op, int fRoot, int gRoot) =>
            op switch
            {
                ZddOperation.Meet => Meet(manager, fRoot, gRoot),
                ZddOperation.SupersetsOf
                    or ZddOperation.SubsetsOf
                    or ZddOperation.NonSubsetsOf
                    or ZddOperation.NonSupersetsOf => Filter(manager, op, fRoot, gRoot),
                _ => throw Unsupported(op),
            };

        // ---- Filters (Restrict / Permit and their negations) ----

        /// <summary>Filters the elements of <c>f</c> according to <paramref name="op"/> and returns the result's root node id.</summary>
        /// <remarks>The result is always a sub-family of <c>f</c>; <see cref="MergeSides"/> relies on this.</remarks>
        private static int Filter(ZddManager manager, ZddOperation op, int fRoot, int gRoot)
        {
            // Terminal combinations settle here before renting a workspace.
            if (TryResolveFilter(op, fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            bool seeksSubsets = SeeksSubsetsInG(op);

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(op, fRoot, gRoot);
                work.PushVisit(rootKey);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    int f = OperationKey.LeftOf(key);
                    int g = OperationKey.RightOf(key);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // Children are already computed (pushed just below this entry, LIFO).
                        NodePair.Split(nodes, f, g, out int level, out int f0, out int f1, out int g0, out int g1);

                        int lo;
                        int hi;

                        if (seeksSubsets)
                        {
                            // Looking for b ⊆ a: candidates without the item only match g0.
                            lo = FilterOf(work, op, f0, g0);
                            hi = MergeSides(manager, work, op, f1, g0, g1);
                        }
                        else
                        {
                            // Looking for a ⊆ b: the merge happens on the item-free side instead.
                            lo = MergeSides(manager, work, op, f0, g0, g1);
                            hi = FilterOf(work, op, f1, g1);
                        }

                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(key, combined);
                        cache.PutBinary(op, f, g, combined);
                        continue;
                    }

                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    if (TryResolveFilter(op, f, g, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    if (cache.TryGetBinary(op, f, g, out int cached))
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

                    if (seeksSubsets)
                    {
                        PushFilter(work, op, childF0, childG0);
                        PushFilter(work, op, childF1, childG0);
                        PushFilter(work, op, childF1, childG1);
                    }
                    else
                    {
                        PushFilter(work, op, childF0, childG0);
                        PushFilter(work, op, childF0, childG1);
                        PushFilter(work, op, childF1, childG1);
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

        /// <summary>Merged answer for a branch where <c>f</c> is filtered against both sides of <c>g</c>.</summary>
        /// <remarks>
        /// If one side of <c>g</c> is empty, no merge is needed: a "keep" filter returns empty
        /// for that side, a "drop" filter returns <c>f</c> unchanged (its result is always a
        /// sub-family of <c>f</c>, so <c>x ∩ f = x</c>).
        /// </remarks>
        private static int MergeSides(
            ZddManager manager,
            OperationWorkspace work,
            ZddOperation op,
            int f,
            int g0,
            int g1)
        {
            if (g1 == NodeTable.Bottom)
            {
                return FilterOf(work, op, f, g0);
            }

            if (g0 == NodeTable.Bottom)
            {
                return FilterOf(work, op, f, g1);
            }

            return BinaryOperations.Apply(
                manager,
                MergeOperationOf(op),
                FilterOf(work, op, f, g0),
                FilterOf(work, op, f, g1));
        }

        /// <summary>Resolves the answer for terminal or identical-family combinations.</summary>
        /// <returns><see langword="true"/> if the answer was resolved.</returns>
        /// <remarks>
        /// Common to all four ops: empty <c>f</c> has no candidates; empty <c>g</c> makes "exists"
        /// false and "forall" vacuously true; <c>f == g</c> always matches. A shortcut involving
        /// <c>{∅}</c> applies only on the searched-in side, since the other direction depends on
        /// whether <c>g</c> happens to contain ∅ and cannot be decided in constant time.
        /// </remarks>
        private static bool TryResolveFilter(ZddOperation op, int f, int g, out int result)
        {
            bool keepsMatches = KeepsMatches(op);

            if (f == NodeTable.Bottom)
            {
                result = NodeTable.Bottom;
                return true;
            }

            if (g == NodeTable.Bottom)
            {
                result = keepsMatches ? NodeTable.Bottom : f;
                return true;
            }

            if (f == g)
            {
                result = keepsMatches ? f : NodeTable.Bottom;
                return true;
            }

            if (SeeksSubsetsInG(op) ? g == NodeTable.Top : f == NodeTable.Top)
            {
                // Whichever side is fixed to ∅, containment always holds, so every candidate matches.
                result = keepsMatches ? f : NodeTable.Bottom;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>Pushes subproblem <c>(f, g)</c>. Pairs resolved in constant time are not pushed.</summary>
        /// <remarks>Must use the same resolution logic as <see cref="FilterOf"/>, or keys will mismatch.</remarks>
        private static void PushFilter(OperationWorkspace work, ZddOperation op, int f, int g)
        {
            if (TryResolveFilter(op, f, g, out _))
            {
                return;
            }

            long key = OperationKey.Of(op, f, g);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>Answer for the already-computed subproblem <c>(f, g)</c>. Pairs with <see cref="PushFilter"/>.</summary>
        private static int FilterOf(OperationWorkspace work, ZddOperation op, int f, int g)
        {
            if (TryResolveFilter(op, f, g, out int direct))
            {
                return direct;
            }

            work.TryGetResult(OperationKey.Of(op, f, g), out int result);
            return result;
        }

        /// <summary>
        /// Whether this op searches <c>g</c> for a <b>subset</b> of the candidate
        /// (<see cref="ZddOperation.SupersetsOf"/> and <see cref="ZddOperation.NonSupersetsOf"/>);
        /// otherwise it searches for a <b>superset</b>.
        /// </summary>
        private static bool SeeksSubsetsInG(ZddOperation op) =>
            op is ZddOperation.SupersetsOf or ZddOperation.NonSupersetsOf;

        /// <summary>Whether matched candidates are kept (Restrict / Permit) rather than dropped.</summary>
        private static bool KeepsMatches(ZddOperation op) =>
            op is ZddOperation.SupersetsOf or ZddOperation.SubsetsOf;

        /// <summary>Operation used to merge two answers on a converging branch (union for "keep", intersect for "drop").</summary>
        private static ZddOperation MergeOperationOf(ZddOperation op) =>
            KeepsMatches(op) ? ZddOperation.Union : ZddOperation.Intersect;

        // ---- Meet ----

        /// <summary>Computes <c>f ⊓ g = { a ∩ b : a ∈ f, b ∈ g }</c>.</summary>
        /// <remarks>
        /// Unlike the filters, the result is not necessarily a sub-family of <c>f</c>. The
        /// traversal shape matches the product in <see cref="FamilyAlgebraOperations"/>, except
        /// the 3 converging subproblems land on the 0-edge instead of the 1-edge.
        /// </remarks>
        private static int Meet(ZddManager manager, int fRoot, int gRoot)
        {
            if (TryResolveMeet(fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(ZddOperation.Meet, fRoot, gRoot);
                work.PushVisit(rootKey);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    int f = OperationKey.LeftOf(key);
                    int g = OperationKey.RightOf(key);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        NodePair.Split(nodes, f, g, out int level, out int f0, out int f1, out int g0, out int g1);

                        // The item survives in a ∩ b only if both sides include it.
                        int hi = MeetOf(work, f1, g1);

                        // The other three combinations all produce item-free intersections.
                        int lo = MeetOf(work, f0, g0);
                        lo = Combine(manager, lo, MeetOf(work, f0, g1));
                        lo = Combine(manager, lo, MeetOf(work, f1, g0));

                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(key, combined);
                        cache.PutBinary(ZddOperation.Meet, f, g, combined);
                        continue;
                    }

                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    if (TryResolveMeet(f, g, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    if (cache.TryGetBinary(ZddOperation.Meet, f, g, out int cached))
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
                    PushMeet(work, childF0, childG0);
                    PushMeet(work, childF0, childG1);
                    PushMeet(work, childF1, childG0);
                    PushMeet(work, childF1, childG1);
                }

                work.TryGetResult(rootKey, out int result);
                return result;
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>Resolves the answer for Meet when a terminal is involved: <c>∅ ⊓ g = ∅</c>, <c>{∅} ⊓ g = {∅}</c>.</summary>
        /// <returns><see langword="true"/> if the answer was resolved.</returns>
        /// <remarks><c>f == g</c> is not a shortcut here: <c>f ⊓ f</c> introduces new pairwise intersections.</remarks>
        private static bool TryResolveMeet(int f, int g, out int result)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                result = NodeTable.Bottom;
                return true;
            }

            if (f == NodeTable.Top || g == NodeTable.Top)
            {
                result = NodeTable.Top;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>Pushes subproblem <c>(f, g)</c>. Pairs involving ∅ resolve immediately and are not pushed.</summary>
        /// <remarks>Must use the same resolution logic as <see cref="MeetOf"/>.</remarks>
        private static void PushMeet(OperationWorkspace work, int f, int g)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                return;
            }

            long key = OperationKey.Of(ZddOperation.Meet, f, g);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>Answer for the already-computed subproblem <c>(f, g)</c>. Pairs with <see cref="PushMeet"/>.</summary>
        private static int MeetOf(OperationWorkspace work, int f, int g)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                return NodeTable.Bottom;
            }

            work.TryGetResult(OperationKey.Of(ZddOperation.Meet, f, g), out int result);
            return result;
        }

        /// <summary>Merges via one Union call, using its own rented workspace so it doesn't disturb this one's stack.</summary>
        private static int Combine(ZddManager manager, int f, int g) =>
            BinaryOperations.Apply(manager, ZddOperation.Union, f, g);

        private static ArgumentOutOfRangeException Unsupported(ZddOperation op) =>
            new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the containment operations " +
                "(Meet / SupersetsOf / SubsetsOf / NonSubsetsOf / NonSupersetsOf).");
    }
}

using System;
using System.Diagnostics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Set operations combining two families (<see cref="ZddOperation.Union"/> /
    /// <see cref="ZddOperation.Intersect"/> / <see cref="ZddOperation.Difference"/> /
    /// <see cref="ZddOperation.SymmetricDifference"/>).
    /// </summary>
    /// <remarks>
    /// Same iterative-traversal template as <see cref="UnaryOperations.Apply"/>, but subproblems
    /// are pairs of nodes rather than single nodes (packed by <see cref="OperationKey.Of"/>).
    /// When operands branch at different levels, the lower family has no sets containing that
    /// item, so the upper family's 1-edge either passes through unchanged or is dropped entirely
    /// (<see cref="Decompose"/>) — the one point where the four ops differ.
    /// </remarks>
    internal static class BinaryOperations
    {
        /// <summary>Sentinel level meaning "no node created; use the lo branch's answer directly".</summary>
        /// <remarks>Level 0 belongs to terminals and is never produced by this operation.</remarks>
        private const int NoNode = 0;

        /// <summary>Applies a set operation to two families and returns the resulting root node id.</summary>
        /// <param name="manager">Manager owning both families.</param>
        /// <param name="op">One of the set operations.</param>
        /// <param name="fRoot">Root node id of the left operand.</param>
        /// <param name="gRoot">Root node id of the right operand.</param>
        /// <returns>Root node id of the resulting family.</returns>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> is disposed.</exception>
        public static int Apply(ZddManager manager, ZddOperation op, int fRoot, int gRoot)
        {
            Debug.Assert(
                op is ZddOperation.Union
                    or ZddOperation.Intersect
                    or ZddOperation.Difference
                    or ZddOperation.SymmetricDifference,
                $"'{op}' is not one of the set operations.");

            // Terminal combinations settle here before renting a workspace.
            if (TryResolveTerminal(op, fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

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
                        Decompose(op, nodes, f, g, out int level, out long loKey, out long hiKey, out int hiId);

                        work.TryGetResult(loKey, out int loResult);

                        int combined;
                        if (level == NoNode)
                        {
                            combined = loResult;
                        }
                        else
                        {
                            // Hi branch is either a computed subproblem or an existing node passed through.
                            int hiResult = hiId;
                            if (hiKey != OperationKey.None)
                            {
                                work.TryGetResult(hiKey, out hiResult);
                            }

                            combined = table.GetNode(level, loResult, hiResult);
                        }

                        work.SetResult(key, combined);
                        cache.PutBinary(op, f, g, combined);
                        continue;
                    }

                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    if (TryResolveTerminal(op, f, g, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    if (cache.TryGetBinary(op, f, g, out int cached))
                    {
                        work.SetResult(key, cached);
                        continue;
                    }

                    Decompose(op, nodes, f, g, out _, out long childLoKey, out long childHiKey, out _);

                    work.PushCombine(key);

                    if (!work.HasResult(childLoKey))
                    {
                        work.PushVisit(childLoKey);
                    }

                    if (childHiKey != OperationKey.None && !work.HasResult(childHiKey))
                    {
                        work.PushVisit(childHiKey);
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

        /// <summary>Resolves the answer when a terminal is involved (<c>f == g</c>, or either is ∅).</summary>
        /// <returns><see langword="true"/> if the answer was resolved.</returns>
        /// <remarks><c>{∅}</c> is not a constant-time shortcut for these ops: resolving it still requires walking to the 0-edge's end.</remarks>
        private static bool TryResolveTerminal(ZddOperation op, int f, int g, out int result)
        {
            if (f == g)
            {
                result = op is ZddOperation.Union or ZddOperation.Intersect ? f : NodeTable.Bottom;
                return true;
            }

            if (f == NodeTable.Bottom)
            {
                result = op is ZddOperation.Union or ZddOperation.SymmetricDifference ? g : NodeTable.Bottom;
                return true;
            }

            if (g == NodeTable.Bottom)
            {
                result = op == ZddOperation.Intersect ? NodeTable.Bottom : f;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>Decomposes subproblem <c>(f, g)</c> by one level. The only part that differs per operation.</summary>
        /// <param name="op">Operation kind.</param>
        /// <param name="nodes">Node table.</param>
        /// <param name="f">Left operand's node id.</param>
        /// <param name="g">Right operand's node id.</param>
        /// <param name="level">Level of the combined node; <see cref="NoNode"/> means no node is created and <paramref name="loKey"/>'s answer is used directly.</param>
        /// <param name="loKey">Key of the 0-edge subproblem; always valid.</param>
        /// <param name="hiKey">Key of the 1-edge subproblem, or <see cref="OperationKey.None"/> if the 1-edge is simply <paramref name="hiId"/>.</param>
        /// <param name="hiId">1-edge node id to use when <paramref name="hiKey"/> is <see cref="OperationKey.None"/>.</param>
        private static void Decompose(
            ZddOperation op,
            NodeTable nodes,
            int f,
            int g,
            out int level,
            out long loKey,
            out long hiKey,
            out int hiId)
        {
            Debug.Assert(
                !NodeTable.IsTerminal(f) || !NodeTable.IsTerminal(g),
                "A pair of terminals is always settled by TryResolveTerminal and never reaches Decompose.");

            int fLevel = NodeTable.IsTerminal(f) ? 0 : nodes[f].Level;
            int gLevel = NodeTable.IsTerminal(g) ? 0 : nodes[g].Level;

            if (fLevel == gLevel)
            {
                // Same branching item: match 0-edges and 1-edges up.
                int fLo;
                int fHi;
                {
                    ref ZddNode node = ref nodes[f];
                    fLo = node.Lo;
                    fHi = node.Hi;
                }

                int gLo;
                int gHi;
                {
                    ref ZddNode node = ref nodes[g];
                    gLo = node.Lo;
                    gHi = node.Hi;
                }

                level = fLevel;
                loKey = OperationKey.Of(op, fLo, gLo);
                hiKey = OperationKey.Of(op, fHi, gHi);
                hiId = NodeTable.Bottom;
                return;
            }

            // One operand's root is at a higher level; the lower one never mentions that item,
            // so nothing in it intersects the upper operand's 1-edge.
            bool fIsUpper = fLevel > gLevel;
            int upper = fIsUpper ? f : g;
            int lower = fIsUpper ? g : f;

            int upperLo;
            int upperHi;
            {
                ref ZddNode node = ref nodes[upper];
                upperLo = node.Lo;
                upperHi = node.Hi;
            }

            // Whether the upper operand's 1-edge (its item-containing sets) survives in the result.
            bool keepsUpperHi = op switch
            {
                ZddOperation.Union or ZddOperation.SymmetricDifference => true,
                ZddOperation.Intersect => false,

                // f \ g: if f is upper, its 1-edge is unaffected by g; if g is upper, g's
                // 1-edge removes nothing from f, so only g's 0-edge matters.
                ZddOperation.Difference => fIsUpper,

                _ => throw Unsupported(op),
            };

            // Difference is the only non-commutative op, so keep operand order on recursion.
            loKey = fIsUpper ? OperationKey.Of(op, upperLo, lower) : OperationKey.Of(op, lower, upperLo);
            hiKey = OperationKey.None;

            if (keepsUpperHi)
            {
                level = fIsUpper ? fLevel : gLevel;
                hiId = upperHi;
            }
            else
            {
                level = NoNode;
                hiId = NodeTable.Bottom;
            }
        }

        private static ArgumentOutOfRangeException Unsupported(ZddOperation op) =>
            new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the set operations (Union / Intersect / Difference / SymmetricDifference).");
    }
}

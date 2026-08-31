using System;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Entry point for running an <see cref="IDdEval{TValue}"/> bottom-up over a ZDD.
    /// <see cref="Zdd.Count"/>, <see cref="Zdd.CountApprox"/>, and <see cref="Zdd.CountBySize"/>
    /// all build on this single traversal.
    /// </summary>
    /// <remarks>
    /// One traversal implementation is shared by every bottom-up DP over the DAG; only the
    /// terminal values and combine step differ, supplied via <see cref="IDdEval{TValue}"/>.
    /// Traversal uses <see cref="OperationWorkspace"/>'s explicit stack, not recursion, since ZDD
    /// depth equals the variable count and a naive recursive walk can overflow the stack.
    /// Each node is evaluated once (memoized by node ID, scoped to a single call); the operation
    /// cache is not used here since it can only hold <c>int</c> node IDs, not arbitrary <c>TValue</c>.
    /// </remarks>
    public static class ZddEvaluation
    {
        /// <summary>Initial size of the value-accumulation table; doubles when it runs out.</summary>
        private const int InitialValueCapacity = 16;

        /// <summary>Evaluates the family bottom-up with <paramref name="eval"/> and returns the root's value.</summary>
        /// <typeparam name="TEval">
        /// The evaluator type. Must be a <c>struct</c>; an interface-typed evaluator would make
        /// every per-node call virtual, several times slower.
        /// </typeparam>
        /// <typeparam name="TValue">The evaluation result type.</typeparam>
        /// <param name="zdd">The family to evaluate.</param>
        /// <param name="eval">The evaluator, passed by value; one copy is used for the whole call.</param>
        /// <returns>The value at the root.</returns>
        /// <remarks>
        /// <typeparamref name="TValue"/> cannot be inferred (constraints don't participate in
        /// inference), so both type arguments must be given explicitly at the call site.
        /// Cost is O(m) calls to <see cref="IDdEval{TValue}.EvalNode"/>, where m is the number of
        /// reachable nodes; the only extra allocation is the value table itself.
        /// An exception thrown by <paramref name="eval"/> propagates to the caller; rented
        /// workspace is still returned in that case.
        /// </remarks>
        /// <exception cref="InvalidOperationException"><paramref name="zdd"/> is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public static TValue Evaluate<TEval, TValue>(this in Zdd zdd, TEval eval)
            where TEval : struct, IDdEval<TValue>
        {
            ZddManager manager = zdd.Manager;

            NodeTable nodes = manager.Table.Nodes;

            // Called once regardless of family shape; every terminal reuses this value.
            TValue falseValue = eval.EvalTerminal(false);
            TValue trueValue = eval.EvalTerminal(true);

            int rootId = zdd.Id;
            if (NodeTable.IsTerminal(rootId))
            {
                return rootId == NodeTable.Top ? trueValue : falseValue;
            }

            // Per-node evaluated values; the result table stores indices into this array.
            TValue[] values = new TValue[InitialValueCapacity];
            int valueCount = 0;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                work.PushVisit(rootId);

                while (work.TryPop(out long entry))
                {
                    int id = (int)OperationWorkspace.KeyOf(entry);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // Children are guaranteed to be resolved already (LIFO: combine was
                        // pushed before its children). Read node fields before calling into
                        // user code (EvalNode), since a ref could be invalidated by it.
                        int level;
                        int lo;
                        int hi;
                        {
                            ref ZddNode node = ref nodes[id];
                            level = node.Level;
                            lo = node.Lo;
                            hi = node.Hi;
                        }

                        TValue value = eval.EvalNode(
                            manager.ItemOf(level),
                            ChildValue(work, values, falseValue, trueValue, lo),
                            ChildValue(work, values, falseValue, trueValue, hi));

                        if (valueCount == values.Length)
                        {
                            Array.Resize(ref values, values.Length * 2);
                        }

                        values[valueCount] = value;
                        work.SetResult(id, valueCount);
                        valueCount++;
                        continue;
                    }

                    // Another parent already resolved this node.
                    if (work.HasResult(id))
                    {
                        continue;
                    }

                    // Descend one level: push self, then unresolved children (terminals resolve
                    // immediately via EvalTerminal, so they're never pushed).
                    int childLo;
                    int childHi;
                    {
                        ref ZddNode node = ref nodes[id];
                        childLo = node.Lo;
                        childHi = node.Hi;
                    }

                    work.PushCombine(id);

                    if (!NodeTable.IsTerminal(childLo) && !work.HasResult(childLo))
                    {
                        work.PushVisit(childLo);
                    }

                    if (!NodeTable.IsTerminal(childHi) && !work.HasResult(childHi))
                    {
                        work.PushVisit(childHi);
                    }
                }

                if (!work.TryGetResult(rootId, out int slot))
                {
                    // The root is non-terminal, so it must have gone through combine; getting
                    // here means the traversal is broken.
                    ThrowHelper.ThrowInvalidOperationException(
                        $"The evaluation of node {rootId} finished without producing a value.");
                }

                return values[slot];
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>Looks up a child's evaluated value: the terminal value, or the value table entry if already computed.</summary>
        private static TValue ChildValue<TValue>(
            OperationWorkspace work,
            TValue[] values,
            TValue falseValue,
            TValue trueValue,
            int childId)
        {
            if (NodeTable.IsTerminal(childId))
            {
                return childId == NodeTable.Top ? trueValue : falseValue;
            }

            if (!work.TryGetResult(childId, out int slot))
            {
                // Children are always resolved before their parent's combine step; getting here
                // means the traversal is broken.
                ThrowHelper.ThrowInvalidOperationException(
                    $"The child node {childId} was evaluated after its parent instead of before it.");
            }

            return values[slot];
        }
    }
}

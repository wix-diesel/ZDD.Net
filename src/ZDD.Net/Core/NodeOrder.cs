using System;
using System.Collections.Generic;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// The non-terminal nodes reachable from a root, ordered so children always
    /// come before their parents (postorder). Backs per-node DP tables stored as
    /// a flat array plus an index.
    /// </summary>
    /// <remarks>
    /// Iterating front-to-back is bottom-up (children resolve first); back-to-front is
    /// top-down. The last entry is always the root. Traversal uses an explicit stack
    /// instead of recursion, since ZDD depth equals the variable count and a naive
    /// recursive walk can overflow the stack on large families (uncatchable in .NET).
    /// </remarks>
    internal sealed class NodeOrder
    {
        /// <summary>Initial depth of the explicit stack; doubles when it runs out.</summary>
        private const int InitialStackCapacity = 32;

        private readonly int[] _ids;
        private readonly Dictionary<int, int> _slots;

        private NodeOrder(int[] ids, Dictionary<int, int> slots)
        {
            _ids = ids;
            _slots = slots;
        }

        /// <summary>Number of non-terminal nodes in the order.</summary>
        public int Count => _ids.Length;

        /// <summary>Node IDs ordered so children precede parents; the last entry is the root.</summary>
        public ReadOnlySpan<int> Ids => _ids;

        /// <summary>
        /// Orders the non-terminal nodes reachable from <paramref name="rootId"/>.
        /// A terminal root yields an empty order.
        /// </summary>
        /// <param name="manager">The manager that owns the family.</param>
        /// <param name="rootId">The family's root node ID.</param>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> has been disposed.</exception>
        public static NodeOrder Build(ZddManager manager, int rootId)
        {
            NodeTable nodes = manager.Table.Nodes;

            List<int> ids = new List<int>();
            Dictionary<int, int> slots = new Dictionary<int, int>();

            if (NodeTable.IsTerminal(rootId))
            {
                return new NodeOrder(Array.Empty<int>(), slots);
            }

            // Two kinds of stack entries, told apart by sign: non-negative means "descend into
            // this node", negative (bit-complemented) means "children are done, emit this node".
            // Non-terminal IDs are always >= 2, so complementing always yields a negative value.
            int[] stack = new int[InitialStackCapacity];
            int top = 0;

            Push(ref stack, ref top, rootId);

            while (top > 0)
            {
                int entry = stack[--top];

                if (entry < 0)
                {
                    int id = ~entry;
                    slots[id] = ids.Count;
                    ids.Add(id);
                    continue;
                }

                // Another parent already handled this node.
                if (slots.ContainsKey(entry))
                {
                    continue;
                }

                int lo;
                int hi;
                {
                    ref ZddNode node = ref nodes[entry];
                    lo = node.Lo;
                    hi = node.Hi;
                }

                // Push self first, then unresolved children on top (LIFO, so children finish first).
                Push(ref stack, ref top, ~entry);

                if (!NodeTable.IsTerminal(lo) && !slots.ContainsKey(lo))
                {
                    Push(ref stack, ref top, lo);
                }

                if (!NodeTable.IsTerminal(hi) && !slots.ContainsKey(hi))
                {
                    Push(ref stack, ref top, hi);
                }
            }

            return new NodeOrder(ids.ToArray(), slots);
        }

        /// <summary>The DP-table index for <paramref name="id"/>.</summary>
        /// <param name="id">A non-terminal node ID reachable from the root this order was built for.</param>
        /// <exception cref="InvalidOperationException"><paramref name="id"/> is not in the order.</exception>
        public int SlotOf(int id)
        {
            if (!_slots.TryGetValue(id, out int slot))
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The node {id} is not reachable from the root this order was built for.");
            }

            return slot;
        }

        private static void Push(ref int[] stack, ref int top, int value)
        {
            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = value;
        }
    }
}

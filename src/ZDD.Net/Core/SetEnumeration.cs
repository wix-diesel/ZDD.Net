using System;
using System.Collections.Generic;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Lazily enumerates the sets belonging to a family, one at a time.
    /// Backs <see cref="Zdd.GetEnumerator"/> / <see cref="Zdd.Sets(ZddEnumerationOrder)"/>.
    /// </summary>
    /// <remarks>
    /// Unlike counting, enumeration cost is proportional to the number of sets returned, not
    /// node count, so results are yielded lazily (e.g. <c>Take(10)</c> stays cheap). The
    /// traversal is iterative (explicit stack), and does not rent a workspace from the manager
    /// since <c>yield return</c> can suspend across other operations. Each returned array is a
    /// fresh copy (docs/ROADMAP.md M1-13) to avoid callers observing a shared, mutated buffer.
    /// <para>
    /// <see cref="SetSpanEnumerator"/> (M6-2, backing <see cref="Zdd.EnumerateInto"/>) is the same
    /// depth-first traversal rewritten as a hand-driven state machine — a <c>ref struct</c> cannot
    /// be an iterator's local, so it cannot reuse <see cref="Traverse"/> directly — sharing the
    /// stack/path constants and the <see cref="Push"/>/<see cref="Append"/> helpers here. This type
    /// is left untouched so <see cref="Sets"/> keeps its exact existing behavior.
    /// </para>
    /// </remarks>
    internal static class SetEnumeration
    {
        /// <summary>Initial depth of the explicit stack; doubles on demand. Shared with <see cref="SetSpanEnumerator"/>.</summary>
        internal const int InitialStackCapacity = 32;

        /// <summary>Initial size of the working buffers that accumulate the path and 0-edge chain. Shared with <see cref="SetSpanEnumerator"/>.</summary>
        internal const int InitialPathCapacity = 16;

        /// <summary>Marker meaning "pop the last item off the path". Node ids are non-negative, so no collision. Shared with <see cref="SetSpanEnumerator"/>.</summary>
        internal const int PopItem = -1;

        /// <summary>Creates a lazy enumeration of the sets in a family, in <paramref name="order"/>.</summary>
        /// <param name="manager">Manager owning the family.</param>
        /// <param name="rootId">Root node id of the family.</param>
        /// <param name="order">Order in which to return sets.</param>
        /// <remarks>
        /// Argument validation happens eagerly here, not inside the iterator body, so it surfaces
        /// at the call to <c>Sets()</c> rather than at the first <c>MoveNext</c>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="order"/> is not a defined value.</exception>
        /// <exception cref="ObjectDisposedException"><paramref name="manager"/> is disposed.</exception>
        public static IEnumerable<int[]> Enumerate(ZddManager manager, int rootId, ZddEnumerationOrder order)
        {
            EnsureDefinedOrder(order);

            // Triggers ObjectDisposedException here rather than once enumeration starts.
            _ = manager.Table;

            return Traverse(manager, rootId, order == ZddEnumerationOrder.Lexicographic);
        }

        /// <summary>Validates that <paramref name="order"/> is a defined value.</summary>
        /// <param name="order">Order to validate.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="order"/> is not a defined value.</exception>
        public static void EnsureDefinedOrder(ZddEnumerationOrder order)
        {
            if (order is not (ZddEnumerationOrder.Default or ZddEnumerationOrder.Lexicographic))
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(order),
                    $"'{nameof(order)}' must be a defined {nameof(ZddEnumerationOrder)} value, but was {(int)order}.");
            }
        }

        /// <summary>Walks depth-first from the root to terminal ⊤, yielding the items on the path each time it arrives.</summary>
        /// <remarks>
        /// Stack entries are distinguished by sign: non-negative is a node id to descend into,
        /// <see cref="PopItem"/> removes the path's last item, any other negative value
        /// <c>-(item + 2)</c> appends an item to the path (push/pop pairs always match).
        /// In <see cref="ZddEnumerationOrder.Lexicographic"/> order, the 0-edge chain from each
        /// node is walked first (shortest/empty-prefix sets sort first), then each node on that
        /// chain descends into its 1-edge, root-side first.
        /// </remarks>
        private static IEnumerable<int[]> Traverse(ZddManager manager, int rootId, bool lexicographic)
        {
            NodeTable nodes = manager.Table.Nodes;

            int[] stack = new int[InitialStackCapacity];
            int top = 0;

            // Items on the current path, root-side first (so always ascending).
            int[] path = new int[InitialPathCapacity];
            int pathLength = 0;

            // Scratch buffer for the 0-edge chain; only used in lexicographic order.
            int[] chain = lexicographic ? new int[InitialPathCapacity] : Array.Empty<int>();

            Push(ref stack, ref top, rootId);

            while (top > 0)
            {
                int entry = stack[--top];

                if (entry == PopItem)
                {
                    pathLength--;
                    continue;
                }

                if (entry < 0)
                {
                    Append(ref path, ref pathLength, -entry - 2);
                    continue;
                }

                // A path reaching ⊥ produces no set.
                if (entry == NodeTable.Bottom)
                {
                    continue;
                }

                if (!lexicographic)
                {
                    if (entry == NodeTable.Top)
                    {
                        yield return path.AsSpan(0, pathLength).ToArray();
                        continue;
                    }

                    ZddNode node = nodes[entry];

                    Push(ref stack, ref top, PopItem);
                    Push(ref stack, ref top, node.Hi);
                    Push(ref stack, ref top, -(manager.ItemOf(node.Level) + 2));
                    Push(ref stack, ref top, node.Lo);
                    continue;
                }

                int chainLength = 0;
                int id = entry;
                while (!NodeTable.IsTerminal(id))
                {
                    Append(ref chain, ref chainLength, id);
                    id = nodes[id].Lo;
                }

                // The 0-edge chain ending at ⊤ means this sub-family contains the empty set (sorts first).
                if (id == NodeTable.Top)
                {
                    yield return path.AsSpan(0, pathLength).ToArray();
                }

                // Descend into 1-edges root-side first, so push from the tail.
                for (int i = chainLength - 1; i >= 0; i--)
                {
                    ZddNode node = nodes[chain[i]];

                    Push(ref stack, ref top, PopItem);
                    Push(ref stack, ref top, node.Hi);
                    Push(ref stack, ref top, -(manager.ItemOf(node.Level) + 2));
                }
            }
        }

        /// <summary>Pushes onto an explicit stack, doubling it on demand. Shared with <see cref="SetSpanEnumerator"/>.</summary>
        internal static void Push(ref int[] stack, ref int top, int entry)
        {
            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = entry;
        }

        /// <summary>Appends to a growable buffer (the path or the 0-edge chain), doubling it on demand. Shared with <see cref="SetSpanEnumerator"/>.</summary>
        internal static void Append(ref int[] buffer, ref int length, int value)
        {
            if (length == buffer.Length)
            {
                // The unused side is held as an empty array, so it never grows via doubling.
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, InitialPathCapacity));
            }

            buffer[length++] = value;
        }
    }
}

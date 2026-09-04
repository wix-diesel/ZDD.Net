using System;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Enumerates a family's sets into a caller-provided buffer, one at a time, without allocating
    /// an array per set. Returned by <see cref="Zdd.EnumerateInto"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Current"/> is a view over the buffer passed to <see cref="Zdd.EnumerateInto"/>;
    /// its contents are overwritten by the next <see cref="MoveNext"/> call. Copy it (e.g.
    /// <c>Current.ToArray()</c>) before advancing if you need to keep it.
    /// </para>
    /// <para>
    /// A <c>ref struct</c> on purpose: it cannot be boxed, stored in a field, captured by a
    /// lambda, or passed to <see cref="System.Collections.Generic.IEnumerable{T}"/>/LINQ, all of
    /// which would let the shared buffer escape past the iteration that owns it. <c>foreach</c>
    /// still works — <see cref="GetEnumerator"/> satisfies the compiler's duck-typed pattern
    /// without implementing <see cref="System.Collections.Generic.IEnumerator{T}"/> (which a
    /// <c>ref struct</c> cannot do).
    /// </para>
    /// <para>
    /// Implementation: the same depth-first traversal as <see cref="SetEnumeration"/>'s
    /// <c>Sets()</c> iterator, rewritten as a hand-driven state machine over an explicit stack
    /// instead of <c>yield return</c> (a <c>ref struct</c> cannot be an iterator's local, so it
    /// cannot suspend inside one). <see cref="MoveNext"/> runs the same loop body <c>Sets()</c>
    /// runs between one <c>yield return</c> and the next, then returns as soon as a set is ready
    /// instead of suspending a compiler-generated coroutine.
    /// </para>
    /// </remarks>
    public ref struct SetSpanEnumerator
    {
        private readonly ZddManager _manager;
        private readonly NodeTable _nodes;
        private readonly Span<int> _buffer;
        private readonly bool _lexicographic;

        private int[] _stack;
        private int _top;

        private int _pathLength;
        private int _currentLength;

        // Scratch buffer for the 0-edge chain; only used in lexicographic order.
        private int[] _chain;

        internal SetSpanEnumerator(ZddManager manager, int rootId, Span<int> buffer, bool lexicographic)
        {
            _manager = manager;
            _nodes = manager.Table.Nodes;
            _buffer = buffer;
            _lexicographic = lexicographic;

            _stack = new int[SetEnumeration.InitialStackCapacity];
            _top = 0;

            _pathLength = 0;
            _currentLength = 0;

            _chain = lexicographic ? new int[SetEnumeration.InitialPathCapacity] : Array.Empty<int>();

            SetEnumeration.Push(ref _stack, ref _top, rootId);
        }

        /// <summary>The most recently produced set, ascending item indices. Overwritten by the next <see cref="MoveNext"/>.</summary>
        public readonly ReadOnlySpan<int> Current => _buffer[.._currentLength];

        /// <summary>Advances to the next set, if any.</summary>
        /// <returns><see langword="true"/> if a set was produced (see <see cref="Current"/>); <see langword="false"/> once enumeration is finished.</returns>
        /// <remarks>
        /// Mirrors <see cref="SetEnumeration"/>'s <c>Traverse</c> exactly, just without a compiler
        /// generated coroutine: each loop body below is what runs between one <c>yield return</c>
        /// and the next there, and this method returns the moment a set is ready instead of
        /// suspending.
        /// </remarks>
        public bool MoveNext()
        {
            while (_top > 0)
            {
                int entry = _stack[--_top];

                if (entry == SetEnumeration.PopItem)
                {
                    _pathLength--;
                    continue;
                }

                if (entry < 0)
                {
                    // The buffer is guaranteed at least MaxSetSize long (checked in
                    // Zdd.EnumerateInto), and the path can never hold more items than that, so
                    // this never runs past the end — unlike SetEnumeration.Traverse's path array,
                    // no growing is needed here.
                    _buffer[_pathLength++] = -entry - 2;
                    continue;
                }

                // A path reaching ⊥ produces no set.
                if (entry == NodeTable.Bottom)
                {
                    continue;
                }

                if (!_lexicographic)
                {
                    if (entry == NodeTable.Top)
                    {
                        _currentLength = _pathLength;
                        return true;
                    }

                    ZddNode node = _nodes[entry];

                    SetEnumeration.Push(ref _stack, ref _top, SetEnumeration.PopItem);
                    SetEnumeration.Push(ref _stack, ref _top, node.Hi);
                    SetEnumeration.Push(ref _stack, ref _top, -(_manager.ItemOf(node.Level) + 2));
                    SetEnumeration.Push(ref _stack, ref _top, node.Lo);
                    continue;
                }

                int chainLength = 0;
                int id = entry;
                while (!NodeTable.IsTerminal(id))
                {
                    SetEnumeration.Append(ref _chain, ref chainLength, id);
                    id = _nodes[id].Lo;
                }

                // Descend into 1-edges root-side first, so push from the tail.
                for (int i = chainLength - 1; i >= 0; i--)
                {
                    ZddNode node = _nodes[_chain[i]];

                    SetEnumeration.Push(ref _stack, ref _top, SetEnumeration.PopItem);
                    SetEnumeration.Push(ref _stack, ref _top, node.Hi);
                    SetEnumeration.Push(ref _stack, ref _top, -(_manager.ItemOf(node.Level) + 2));
                }

                // The 0-edge chain ending at ⊤ means this sub-family contains the empty set (sorts first).
                if (id == NodeTable.Top)
                {
                    _currentLength = _pathLength;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Returns this enumerator itself, satisfying the <c>foreach</c> pattern.</summary>
        public readonly SetSpanEnumerator GetEnumerator() => this;
    }
}

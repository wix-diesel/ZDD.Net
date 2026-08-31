using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Scratch space for the iterative (explicit-stack) implementation of operations: a work
    /// stack plus an intermediate-result table, standing in for recursion for a single operation.
    /// All ZDD operations are built on this type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recursive implementations would overflow the stack at realistic variable counts, which
    /// .NET cannot catch (the process dies), so every operation is written iteratively. See
    /// <see cref="UnaryOperations.Apply"/> for the reference shape: push the root
    /// (<see cref="PushVisit"/>); pop an entry; if it's a "combine" (<see cref="IsCombine"/>),
    /// merge its children's results via <see cref="UniqueTable.GetNode"/> and
    /// <see cref="SetResult"/>; otherwise, if the result isn't already known
    /// (<see cref="TryGetResult"/>) or resolvable as a base case or from the
    /// <see cref="OperationCache"/>, re-push self with <see cref="PushCombine"/> then push its
    /// unresolved children — LIFO order guarantees children are handled before the combine.
    /// </para>
    /// <para>
    /// Stack and result-table entries are <c>long</c> keys: a node ID for unary ops, or two
    /// node IDs packed 32 bits each for binary ops (both non-negative). A negative key marks a
    /// "combine" entry (<see cref="PushCombine"/> bitwise-inverts it).
    /// </para>
    /// <para>
    /// Unlike the lossy, cross-operation <see cref="OperationCache"/>, the intermediate-result
    /// table never drops entries within a single operation — losing one could make two children
    /// evict each other's slot forever, so it never terminates.
    /// </para>
    /// <para>
    /// One instance is reused across operations via <see cref="Reset"/>, which just advances a
    /// generation counter instead of clearing the arrays, so cleanup cost is O(1) regardless of
    /// how large the previous operation was. Not thread-safe.
    /// </para>
    /// </remarks>
    internal sealed class OperationWorkspace
    {
        /// <summary>Default initial depth of the work stack.</summary>
        public const int DefaultStackCapacity = 64;

        /// <summary>Default initial entry count of the intermediate-result table (power of two).</summary>
        public const int DefaultResultCapacity = 64;

        /// <summary>Minimum entry count of the intermediate-result table (power of two, greater than 2).</summary>
        public const int MinimumResultCapacity = 4;

        /// <summary>Load factor (%) at which the intermediate-result table grows; matches <see cref="UniqueTable"/>.</summary>
        public const int MaxLoadFactorPercent = 70;

        /// <summary>Maximum entry count of the intermediate-result table (power of two).</summary>
        public const int MaxResultCapacity = 1 << 30;

        /// <summary>Generation value for a slot that has never been written; <see cref="_generation"/> starts at 1.</summary>
        private const int UnusedGeneration = 0;

        /// <summary>Work stack; negative entries are "combine" markers (<see cref="PushCombine"/>).</summary>
        private long[] _stack;

        /// <summary>Number of entries currently on the stack.</summary>
        private int _top;

        /// <summary>Intermediate-result keys; only slots matching <see cref="_generations"/> are valid.</summary>
        private long[] _keys;

        /// <summary>Result node IDs, indexed in parallel with <see cref="_keys"/>.</summary>
        private int[] _values;

        /// <summary>Generation each slot was last written in; a mismatch with <see cref="_generation"/> means empty.</summary>
        private int[] _generations;

        /// <summary>Current generation; <see cref="Reset"/> just increments this.</summary>
        private int _generation;

        /// <summary>Number of entries in the intermediate-result table.</summary>
        private int _count;

        /// <summary>Entry count above which the intermediate-result table grows.</summary>
        private int _growThreshold;

        /// <summary>Creates a workspace with the default sizes.</summary>
        public OperationWorkspace()
            : this(DefaultStackCapacity, DefaultResultCapacity)
        {
        }

        /// <summary>Creates a workspace with the given sizes; both grow automatically as needed.</summary>
        /// <param name="stackCapacity">Initial work-stack depth; must be at least 1.</param>
        /// <param name="resultCapacity">
        /// Initial intermediate-result table size, rounded up to a power of two at least <see cref="MinimumResultCapacity"/>.
        /// </param>
        public OperationWorkspace(int stackCapacity, int resultCapacity)
        {
            ThrowHelper.ThrowIfNegativeOrZero(stackCapacity, nameof(stackCapacity));
            ThrowHelper.ThrowIfNegativeOrZero(resultCapacity, nameof(resultCapacity));

            if (resultCapacity > MaxResultCapacity)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(resultCapacity),
                    $"'{nameof(resultCapacity)}' must not exceed {MaxResultCapacity}, but was {resultCapacity}.");
            }

            int capacity = Math.Max(MinimumResultCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)resultCapacity));

            _stack = new long[stackCapacity];
            _top = 0;
            _keys = new long[capacity];
            _values = new int[capacity];
            _generations = new int[capacity];
            _generation = UnusedGeneration + 1;
            _count = 0;
            _growThreshold = ComputeGrowThreshold(capacity);
        }

        /// <summary>Current work-stack depth.</summary>
        public int Depth => _top;

        /// <summary>Whether the work stack is empty.</summary>
        public bool IsEmpty => _top == 0;

        /// <summary>Number of entries in the intermediate-result table.</summary>
        public int ResultCount => _count;

        /// <summary>Current work-stack capacity (grows automatically).</summary>
        public int StackCapacity => _stack.Length;

        /// <summary>Current intermediate-result table capacity (grows automatically).</summary>
        public int ResultCapacity => _keys.Length;

        /// <summary>Whether a popped entry is a "combine" (re-pushed after its children were queued).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCombine(long entry) => entry < 0;

        /// <summary>The original key of a popped entry, with the combine marker removed.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long KeyOf(long entry) => entry < 0 ? ~entry : entry;

        /// <summary>Pushes a key as a subproblem to visit.</summary>
        /// <param name="key">Subproblem key (non-negative).</param>
        public void PushVisit(long key)
        {
            AssertKey(key);
            Push(key);
        }

        /// <summary>
        /// Pushes a key marked as "combine", to be popped again once its children's results are ready.
        /// Must be called before pushing the children (LIFO order pops children first).
        /// </summary>
        /// <param name="key">Subproblem key (non-negative).</param>
        public void PushCombine(long key)
        {
            AssertKey(key);
            Push(~key);
        }

        /// <summary>Pops one entry from the work stack.</summary>
        /// <param name="entry">The popped entry; decode with <see cref="IsCombine"/> and <see cref="KeyOf"/>.</param>
        /// <returns><see langword="true"/> if an entry was popped, <see langword="false"/> if the stack was empty.</returns>
        public bool TryPop(out long entry)
        {
            if (_top == 0)
            {
                entry = 0;
                return false;
            }

            entry = _stack[--_top];
            return true;
        }

        /// <summary>Looks up the result already computed for a key.</summary>
        /// <param name="key">Subproblem key (non-negative).</param>
        /// <param name="result">Result node ID if found, otherwise <see cref="NodeTable.Bottom"/>.</param>
        /// <returns><see langword="true"/> if already computed.</returns>
        public bool TryGetResult(long key, out int result)
        {
            AssertKey(key);

            long[] keys = _keys;
            int[] generations = _generations;
            int generation = _generation;
            int mask = keys.Length - 1;
            int slot = SlotOf(key, keys.Length);

            while (true)
            {
                if (generations[slot] != generation)
                {
                    // Either a leftover from a previous operation, or never used — either way, empty.
                    result = NodeTable.Bottom;
                    return false;
                }

                if (keys[slot] == key)
                {
                    result = _values[slot];
                    return true;
                }

                slot = (slot + 1) & mask;
            }
        }

        /// <summary>Whether a result is already recorded for the key.</summary>
        /// <param name="key">Subproblem key (non-negative).</param>
        public bool HasResult(long key) => TryGetResult(key, out _);

        /// <summary>
        /// Records a key's result. Re-recording the same key overwrites the prior value, which is
        /// safe since recomputing a subproblem always yields the same answer.
        /// </summary>
        /// <param name="key">Subproblem key (non-negative).</param>
        /// <param name="result">Result node ID.</param>
        public void SetResult(long key, int result)
        {
            AssertKey(key);

            if (_count + 1 > _growThreshold)
            {
                Grow();
            }

            int slot = FindSlot(_keys, _generations, _generation, key);
            if (_generations[slot] != _generation)
            {
                _generations[slot] = _generation;
                _keys[slot] = key;
                _count++;
            }

            _values[slot] = result;
        }

        /// <summary>Clears the workspace for the next operation without releasing allocated arrays.</summary>
        /// <remarks>
        /// Advances the generation counter instead of clearing the tables, so cleanup is O(1)
        /// regardless of how large the previous operation was.
        /// </remarks>
        public void Reset()
        {
            _top = 0;
            _count = 0;

            if (_generation == int.MaxValue)
            {
                // Happens once per ~2.1 billion operations: actually clear and restart generations.
                Array.Clear(_generations);
                _generation = UnusedGeneration + 1;
                return;
            }

            _generation++;
        }

        private static int ComputeGrowThreshold(int capacity) =>
            (int)((long)capacity * MaxLoadFactorPercent / 100);

        /// <summary>Computes the slot index for a key via <see cref="Hashing.Mix64"/> and Fibonacci hashing.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SlotOf(long key, int capacity) =>
            Hashing.IndexForPowerOfTwo(Hashing.Mix64((ulong)key), capacity);

        /// <summary>Finds the slot holding <paramref name="key"/>, or the empty slot it should go in.</summary>
        private static int FindSlot(long[] keys, int[] generations, int generation, long key)
        {
            int mask = keys.Length - 1;
            int slot = SlotOf(key, keys.Length);

            while (true)
            {
                if (generations[slot] != generation || keys[slot] == key)
                {
                    return slot;
                }

                slot = (slot + 1) & mask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Push(long entry)
        {
            long[] stack = _stack;
            if (_top == stack.Length)
            {
                GrowStack();
                stack = _stack;
            }

            stack[_top++] = entry;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowStack()
        {
            int capacity = _stack.Length;
            if (capacity >= Array.MaxLength / 2)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The work stack cannot grow beyond {Array.MaxLength} entries; it currently holds {capacity}.");
            }

            Array.Resize(ref _stack, capacity * 2);
        }

        /// <summary>Doubles the intermediate-result table and reinserts all entries.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            long[] oldKeys = _keys;
            int[] oldValues = _values;
            int[] oldGenerations = _generations;
            int generation = _generation;
            int capacity = oldKeys.Length;

            if (capacity >= MaxResultCapacity)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The intermediate result table cannot grow beyond {MaxResultCapacity} entries; " +
                    $"it currently holds {_count} entr(ies).");
            }

            int newCapacity = capacity * 2;
            long[] keys = new long[newCapacity];
            int[] values = new int[newCapacity];

            // New arrays are zero-initialized, so no slot belongs to the current generation yet;
            // only entries from the current generation need to be copied over.
            int[] generations = new int[newCapacity];

            for (int i = 0; i < oldKeys.Length; i++)
            {
                if (oldGenerations[i] != generation)
                {
                    continue;
                }

                long key = oldKeys[i];
                int slot = FindSlot(keys, generations, generation, key);
                generations[slot] = generation;
                keys[slot] = key;
                values[slot] = oldValues[i];
            }

            _keys = keys;
            _values = values;
            _generations = generations;
            _growThreshold = ComputeGrowThreshold(newCapacity);
        }

        /// <summary>
        /// Debug-only assertion that a key is non-negative, since a negative key would collide
        /// with the "combine" marker.
        /// </summary>
        [Conditional("DEBUG")]
        private static void AssertKey(long key) =>
            Debug.Assert(key >= 0, $"A workspace key must be non-negative, but was {key}.");
    }
}

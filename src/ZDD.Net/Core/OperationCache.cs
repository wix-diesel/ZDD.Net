using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Memo table for operation results: a direct-mapped lossy cache (CUDD-style) from
    /// <c>(operation, operands)</c> to a result node ID, overwriting on collision.
    /// </summary>
    /// <remarks>
    /// Without memoization, ZDD binary operations revisit the same subproblems along every path,
    /// turning DAG-sized work into path-count-sized work — an exponential blowup. Losing an entry
    /// only costs a recompute (same answer), so no chaining, probing, or eviction policy is needed;
    /// entries are 16-byte structs holding the full untruncated key, so hash collisions can only
    /// cause a miss, never a wrong hit. Not thread-safe.
    /// </remarks>
    internal sealed class OperationCache
    {
        /// <summary>Nodes handled per entry; default size = node count / this value.</summary>
        public const int NodesPerEntry = 4;

        /// <summary>Default initial size (16 bytes x 1024 = 16 KB).</summary>
        public const int DefaultInitialCapacity = 1024;

        /// <summary>Default max size (16 bytes x ~4.19M = 64 MB).</summary>
        public const int DefaultMaxCapacity = 1 << 22;

        /// <summary>
        /// Largest value allowed for <see cref="MaxCapacity"/> (16 bytes x ~134M = 2 GB).
        /// </summary>
        public const int CapacityLimit = 1 << 27;

        /// <summary>Sentinel marking an entry as unused.</summary>
        private const int EmptyOp = (int)ZddOperation.None;

        private readonly int _maxCapacity;

        /// <summary>Length is always 0 or a power of two; 0 means every lookup misses (cache disabled).</summary>
        private Entry[] _entries;

        private long _lookups;
        private long _hits;
        private long _collisions;

        /// <summary>Creates a cache with the default sizes.</summary>
        public OperationCache()
            : this(DefaultInitialCapacity, DefaultMaxCapacity)
        {
        }

        /// <summary>Creates a cache with the given sizes.</summary>
        /// <param name="initialCapacity">
        /// Initial entry count, rounded up to a power of two and clamped to <paramref name="maxCapacity"/>.
        /// 0 leaves the table unallocated until <see cref="Tune"/> is called.
        /// </param>
        /// <param name="maxCapacity">
        /// Entry count ceiling, rounded down to a power of two. 0 disables the cache entirely.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Either value is negative, or <paramref name="maxCapacity"/> exceeds <see cref="CapacityLimit"/>.
        /// </exception>
        public OperationCache(int initialCapacity, int maxCapacity)
        {
            ThrowHelper.ThrowIfNegative(initialCapacity, nameof(initialCapacity));
            ThrowHelper.ThrowIfNegative(maxCapacity, nameof(maxCapacity));

            if (maxCapacity > CapacityLimit)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(maxCapacity),
                    $"'{nameof(maxCapacity)}' must not exceed {CapacityLimit}, but was {maxCapacity}.");
            }

            _maxCapacity = maxCapacity == 0 ? 0 : 1 << BitOperations.Log2((uint)maxCapacity);

            uint capacity = initialCapacity == 0
                ? 0
                : Math.Min((uint)_maxCapacity, BitOperations.RoundUpToPowerOf2((uint)initialCapacity));

            _entries = capacity == 0 ? Array.Empty<Entry>() : new Entry[capacity];
        }

        /// <summary>Current entry count (0 or a power of two).</summary>
        public int Capacity => _entries.Length;

        /// <summary>Entry count ceiling for automatic growth; 0 means the cache is disabled.</summary>
        public int MaxCapacity => _maxCapacity;

        /// <summary>Whether the cache can answer lookups (size is nonzero).</summary>
        public bool IsEnabled => _entries.Length != 0;

        /// <summary>Total number of lookups so far.</summary>
        public long Lookups => _lookups;

        /// <summary>Number of lookups that hit.</summary>
        public long Hits => _hits;

        /// <summary>Number of lookups that missed.</summary>
        public long Misses => _lookups - _hits;

        /// <summary>
        /// Number of writes that overwrote a different <c>(operation, operands)</c> entry.
        /// Together with hit rate, indicates whether the size is adequate.
        /// </summary>
        public long Collisions => _collisions;

        /// <summary>Hit rate (0.0-1.0); 0 if never looked up.</summary>
        public double HitRate => _lookups == 0 ? 0.0 : (double)_hits / _lookups;

        /// <summary>Looks up the result of a binary operation; operand order doesn't matter for commutative ops.</summary>
        /// <param name="op">The operation; must not be <see cref="ZddOperation.None"/>.</param>
        /// <param name="f">Left operand node ID.</param>
        /// <param name="g">Right operand node ID.</param>
        /// <param name="result">Result node ID if found, otherwise <see cref="NodeTable.Bottom"/>.</param>
        /// <returns><see langword="true"/> if an entry was found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetBinary(ZddOperation op, int f, int g, out int result)
        {
            AssertBinary(op);
            Normalize(op, ref f, ref g);
            return TryGet(op, f, g, out result);
        }

        /// <summary>Stores the result of a binary operation, unconditionally overwriting the slot's prior occupant.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PutBinary(ZddOperation op, int f, int g, int result)
        {
            AssertBinary(op);
            Normalize(op, ref f, ref g);
            Put(op, f, g, result);
        }

        /// <summary>Looks up the result of a unary operation.</summary>
        /// <param name="op">The operation; must not be <see cref="ZddOperation.None"/>.</param>
        /// <param name="f">Operand node ID.</param>
        /// <param name="item">
        /// Operation parameter (e.g. the item index for <see cref="ZddOperation.Change"/>); pass 0 if unused.
        /// </param>
        /// <param name="result">Result node ID if found, otherwise <see cref="NodeTable.Bottom"/>.</param>
        /// <returns><see langword="true"/> if an entry was found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetUnary(ZddOperation op, int f, int item, out int result)
        {
            AssertUnary(op);
            return TryGet(op, f, item, out result);
        }

        /// <summary>Stores the result of a unary operation, unconditionally overwriting the slot's prior occupant.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PutUnary(ZddOperation op, int f, int item, int result)
        {
            AssertUnary(op);
            Put(op, f, item, result);
        }

        /// <summary>
        /// Discards all entries. Must be called after any operation that changes what node IDs mean
        /// (e.g. a future node-table GC compaction).
        /// </summary>
        /// <remarks>Statistics (<see cref="Lookups"/> etc.) are cumulative and not reset.</remarks>
        public void Clear() => Array.Clear(_entries);

        /// <summary>Resets the statistics counters only; entries are untouched.</summary>
        public void ResetStatistics()
        {
            _lookups = 0;
            _hits = 0;
            _collisions = 0;
        }

        /// <summary>Grows the table to fit the current node count. Called from operation entry points.</summary>
        /// <param name="nodeCount">Current node count.</param>
        /// <returns><see langword="true"/> if the table actually grew.</returns>
        /// <remarks>
        /// Targets <c>nodeCount / <see cref="NodesPerEntry"/></c> entries, capped by
        /// <see cref="MaxCapacity"/>; never shrinks. Growing rebuilds the table from scratch
        /// since direct-mapped slots are invalidated by any capacity change anyway.
        /// </remarks>
        public bool Tune(long nodeCount)
        {
            int capacity = _entries.Length;
            if (capacity >= _maxCapacity)
            {
                return false;
            }

            long desired = nodeCount <= 0 ? 0 : nodeCount / NodesPerEntry;
            if (desired <= capacity)
            {
                return false;
            }

            // desired < _maxCapacity <= CapacityLimit, so rounding up still fits in a uint.
            int grown = desired >= _maxCapacity
                ? _maxCapacity
                : (int)Math.Min((uint)_maxCapacity, BitOperations.RoundUpToPowerOf2((uint)desired));

            _entries = new Entry[grown];
            return true;
        }

        /// <summary>For commutative operations, swaps operands so that <c>a &lt;= b</c>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Normalize(ZddOperation op, ref int a, ref int b)
        {
            if (a > b && ZddOperations.IsCommutative(op))
            {
                (a, b) = (b, a);
            }
        }

        /// <summary>Packs both operands into a 64-bit key without losing information.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long KeyOf(int a, int b) => (long)(((ulong)(uint)a << 32) | (uint)b);

        /// <summary>Computes the slot index for <c>(op, a, b)</c> via <see cref="Hashing.Combine(int, int, int)"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SlotOf(ZddOperation op, int a, int b, int capacity) =>
            (int)(Hashing.Combine((int)op, a, b) & (ulong)(capacity - 1));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryGet(ZddOperation op, int a, int b, out int result)
        {
            _lookups++;

            Entry[] entries = _entries;
            if (entries.Length == 0)
            {
                result = NodeTable.Bottom;
                return false;
            }

            ref Entry entry = ref entries[SlotOf(op, a, b, entries.Length)];

            if (entry.Op == (int)op && entry.Key == KeyOf(a, b))
            {
                _hits++;
                result = entry.Result;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Put(ZddOperation op, int a, int b, int result)
        {
            Entry[] entries = _entries;
            if (entries.Length == 0)
            {
                return;
            }

            long key = KeyOf(a, b);
            ref Entry entry = ref entries[SlotOf(op, a, b, entries.Length)];

            if (entry.Op != EmptyOp && (entry.Op != (int)op || entry.Key != key))
            {
                _collisions++;
            }

            entry.Key = key;
            entry.Op = (int)op;
            entry.Result = result;
        }

        [Conditional("DEBUG")]
        private static void AssertBinary(ZddOperation op) =>
            Debug.Assert(
                op != ZddOperation.None && !ZddOperations.IsUnary(op),
                $"'{op}' is not a binary operation; use the unary entry points for it.");

        [Conditional("DEBUG")]
        private static void AssertUnary(ZddOperation op) =>
            Debug.Assert(
                ZddOperations.IsUnary(op),
                $"'{op}' is not a unary operation; use the binary entry points for it.");

        /// <summary>A single cache entry, 16 bytes fixed. <see cref="Op"/> == <see cref="EmptyOp"/> means unused.</summary>
        internal struct Entry
        {
            /// <summary>Both operands packed 32 bits each, untruncated.</summary>
            public long Key;

            /// <summary>The <see cref="ZddOperation"/> value.</summary>
            public int Op;

            /// <summary>Result node ID; <see cref="NodeTable.Bottom"/> is a valid value.</summary>
            public int Result;
        }
    }
}

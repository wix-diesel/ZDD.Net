using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Node unique table: maps <c>(level, lo, hi)</c> triples to a unique node ID, ensuring
    /// isomorphic subgraphs are never split across multiple nodes. Also applies the
    /// zero-suppression rule (<c>hi == bottom</c> returns <c>lo</c> instead of creating a node),
    /// enforced only in <see cref="GetNode"/>.
    /// </summary>
    /// <remarks>
    /// This is the sole entry point for node creation; calling <see cref="NodeTable.Add"/>
    /// directly can produce duplicate or rule-violating nodes. Uses open addressing (linear
    /// probing) over a power-of-two slot array of node IDs (0 = <see cref="NodeTable.Bottom"/>
    /// marks an empty slot), rather than <c>Dictionary&lt;TKey,TValue&gt;</c>, to avoid boxing
    /// and extra indirection on this hot path. Grows by doubling and rehashing the slot array
    /// past <see cref="MaxLoadFactorPercent"/>% load; the node table itself is untouched, so
    /// existing node IDs remain valid after a grow. Not thread-safe.
    /// </remarks>
    internal sealed class UniqueTable
    {
        /// <summary>Load factor (%) at which the slot array doubles.</summary>
        public const int MaxLoadFactorPercent = 70;

        /// <summary>Minimum slot array size (power of two).</summary>
        public const int MinimumCapacity = 4;

        /// <summary>Default initial slot array size (power of two).</summary>
        public const int DefaultCapacity = 1024;

        /// <summary>
        /// Maximum slot array size: the largest power of two not exceeding <see cref="Array.MaxLength"/>.
        /// </summary>
        public const int MaxCapacity = 1 << 30;

        /// <summary>Sentinel marking an empty slot; real node IDs are always >= 2.</summary>
        private const int EmptySlot = NodeTable.Bottom;

        private readonly NodeTable _nodes;

        /// <summary>Slot to node ID; length is always a power of two.</summary>
        private int[] _slots;

        /// <summary>Registered entry count (equals <c>_nodes.Count</c>).</summary>
        private int _count;

        /// <summary>Entry count above which the table grows.</summary>
        private int _growThreshold;

        /// <summary>Total number of occupied slots skipped over during linear probes.</summary>
        private long _collisions;

        /// <summary>Creates a unique table over a new node table, with the default initial capacity.</summary>
        public UniqueTable()
            : this(new NodeTable(), DefaultCapacity)
        {
        }

        /// <summary>Creates a unique table over a new node table, with the given initial capacity.</summary>
        /// <param name="initialCapacity">Initial slot array size, rounded up to a power of two.</param>
        public UniqueTable(int initialCapacity)
            : this(new NodeTable(), initialCapacity)
        {
        }

        /// <summary>Creates a unique table over an existing (empty) node table.</summary>
        /// <param name="nodes">Backing node storage; must not be <see langword="null"/>.</param>
        /// <param name="initialCapacity">
        /// Initial slot array size, rounded up to a power of two between
        /// <see cref="MinimumCapacity"/> and <see cref="MaxCapacity"/>.
        /// </param>
        /// <remarks>
        /// <paramref name="nodes"/> must be empty — any pre-existing nodes would not be
        /// registered in this table and could be duplicated.
        /// </remarks>
        public UniqueTable(NodeTable nodes, int initialCapacity)
        {
            ThrowHelper.ThrowIfNull(nodes, nameof(nodes));
            ThrowHelper.ThrowIfNegativeOrZero(initialCapacity, nameof(initialCapacity));

            if (initialCapacity > MaxCapacity)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    $"'{nameof(initialCapacity)}' must not exceed {MaxCapacity}, but was {initialCapacity}.");
            }

            if (nodes.Count != 0)
            {
                ThrowHelper.ThrowArgumentException(
                    nameof(nodes),
                    $"The node table must be empty when a unique table is built on top of it, but it already holds {nodes.Count} node(s).");
            }

            int capacity = Math.Max(MinimumCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)initialCapacity));

            _nodes = nodes;
            _slots = new int[capacity];
            _count = 0;
            _growThreshold = ComputeGrowThreshold(capacity);
            _collisions = 0;
        }

        /// <summary>The node table backing this unique table.</summary>
        public NodeTable Nodes => _nodes;

        /// <summary>Registered node count (terminals excluded).</summary>
        public int Count => _count;

        /// <summary>Current slot array size (power of two).</summary>
        public int Capacity => _slots.Length;

        /// <summary>Entry count above which the next insert triggers a grow.</summary>
        public int GrowThreshold => _growThreshold;

        /// <summary>Total number of occupied slots skipped over during linear probes for lookups.</summary>
        /// <remarks>
        /// Counts only <see cref="GetNode"/> / <see cref="TryGetExisting"/> probes, not the
        /// rehashing during a grow or the immediate re-probe in <see cref="FindEmptySlot"/>.
        /// Together with the load factor (<see cref="Count"/> / <see cref="Capacity"/>), this
        /// indicates whether the initial capacity and hash distribution are adequate.
        /// </remarks>
        public long Collisions => _collisions;

        /// <summary>Returns the node ID for <c>(level, lo, hi)</c>, creating it if not already registered.</summary>
        /// <param name="level">Variable level; must exceed the levels of <paramref name="lo"/>/<paramref name="hi"/>.</param>
        /// <param name="lo">0-branch child node ID.</param>
        /// <param name="hi">1-branch child node ID.</param>
        /// <returns>
        /// <paramref name="lo"/> itself if <paramref name="hi"/> is <see cref="NodeTable.Bottom"/>
        /// (zero-suppression); otherwise the existing or newly created node's ID.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="level"/> is non-positive, or <paramref name="lo"/>/<paramref name="hi"/>
        /// is not an existing node ID.
        /// </exception>
        public int GetNode(int level, int lo, int hi)
        {
            ValidateKey(level, lo, hi);

            // Zero-suppression rule: a node whose 1-branch leads to bottom represents no
            // combination that includes this variable, so it's equal to its lo side.
            if (hi == NodeTable.Bottom)
            {
                AssertChildLevel(level, lo, nameof(lo));
                return lo;
            }

            AssertChildLevel(level, lo, nameof(lo));
            AssertChildLevel(level, hi, nameof(hi));

            int[] slots = _slots;
            int mask = slots.Length - 1;
            int slot = Hashing.IndexForPowerOfTwo(Hashing.Combine(level, lo, hi), slots.Length);

            while (true)
            {
                int id = slots[slot];
                if (id == EmptySlot)
                {
                    break;
                }

                ref ZddNode node = ref _nodes[id];
                if (node.Level == level && node.Lo == lo && node.Hi == hi)
                {
                    return id;
                }

                _collisions++;
                slot = (slot + 1) & mask;
            }

            // Reached an empty slot: not yet registered. Grow first, since growing invalidates
            // the slot we just found; allocate the node only after growing succeeds.
            if (_count + 1 > _growThreshold)
            {
                Grow();
                slot = FindEmptySlot(level, lo, hi);
            }

            int newId = _nodes.Add(level, lo, hi);
            _slots[slot] = newId;
            _count++;
            return newId;
        }

        /// <summary>Returns the ID for <c>(level, lo, hi)</c> if already registered, without creating one.</summary>
        /// <returns><see langword="true"/> if registered.</returns>
        public bool TryGetExisting(int level, int lo, int hi, out int id)
        {
            int[] slots = _slots;
            int mask = slots.Length - 1;
            int slot = Hashing.IndexForPowerOfTwo(Hashing.Combine(level, lo, hi), slots.Length);

            while (true)
            {
                int candidate = slots[slot];
                if (candidate == EmptySlot)
                {
                    id = NodeTable.Bottom;
                    return false;
                }

                ref ZddNode node = ref _nodes[candidate];
                if (node.Level == level && node.Lo == lo && node.Hi == hi)
                {
                    id = candidate;
                    return true;
                }

                _collisions++;
                slot = (slot + 1) & mask;
            }
        }

        /// <summary>
        /// Finds the empty slot where <c>(level, lo, hi)</c> belongs. Only valid when the key
        /// is known not to be registered yet (no match check is performed).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindEmptySlot(int level, int lo, int hi)
        {
            int[] slots = _slots;
            int mask = slots.Length - 1;
            int slot = Hashing.IndexForPowerOfTwo(Hashing.Combine(level, lo, hi), slots.Length);

            while (slots[slot] != EmptySlot)
            {
                slot = (slot + 1) & mask;
            }

            return slot;
        }

        /// <summary>Doubles the slot array and rehashes all entries. The node table is untouched.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            int[] old = _slots;
            int capacity = old.Length;

            if (capacity >= MaxCapacity)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The unique table cannot grow beyond {MaxCapacity} slots, which is the largest power of two " +
                    $"that fits in an array. It currently holds {_count} node(s).");
            }

            int newCapacity = capacity * 2;
            int[] grown = new int[newCapacity];
            int mask = newCapacity - 1;

            for (int i = 0; i < old.Length; i++)
            {
                int id = old[i];
                if (id == EmptySlot)
                {
                    continue;
                }

                ref ZddNode node = ref _nodes[id];
                int slot = Hashing.IndexForPowerOfTwo(Hashing.Combine(node.Level, node.Lo, node.Hi), newCapacity);
                while (grown[slot] != EmptySlot)
                {
                    slot = (slot + 1) & mask;
                }

                grown[slot] = id;
            }

            _slots = grown;
            _growThreshold = ComputeGrowThreshold(newCapacity);
        }

        private static int ComputeGrowThreshold(int capacity) =>
            (int)((long)capacity * MaxLoadFactorPercent / 100);

        /// <summary>
        /// Rebuilds the slot array from scratch to match <see cref="Nodes"/>' current contents,
        /// after <see cref="NodeTable.Compact"/> has renumbered every id — the old slots would
        /// otherwise point at the wrong node (or none at all).
        /// </summary>
        /// <remarks>
        /// Sized for <see cref="NodeTable.Count"/> using the same rule as the constructor, so the
        /// load factor right after a collection matches what a freshly built table of that size
        /// would have. No duplicate check is needed while reinserting: compaction only renumbers
        /// nodes, it cannot introduce two nodes with the same <c>(level, lo, hi)</c>, since it never
        /// touches canonicity. Collision and lookup statistics are cumulative and untouched, like
        /// <see cref="OperationCache.Clear"/>'s.
        /// </remarks>
        internal void RebuildAfterCollection()
        {
            int liveCount = _nodes.Count;
            int capacity = Math.Max(MinimumCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(liveCount, 1)));
            while (capacity < MaxCapacity && ComputeGrowThreshold(capacity) < liveCount)
            {
                capacity *= 2;
            }

            int[] rebuilt = new int[capacity];
            int mask = capacity - 1;

            for (int id = NodeTable.FirstNodeId; id < NodeTable.FirstNodeId + liveCount; id++)
            {
                ref ZddNode node = ref _nodes[id];
                int slot = Hashing.IndexForPowerOfTwo(Hashing.Combine(node.Level, node.Lo, node.Hi), capacity);

                while (rebuilt[slot] != EmptySlot)
                {
                    slot = (slot + 1) & mask;
                }

                rebuilt[slot] = id;
            }

            _slots = rebuilt;
            _count = liveCount;
            _growThreshold = ComputeGrowThreshold(capacity);
        }

        /// <summary>Validates that the key is structurally sound (positive level, children exist).</summary>
        /// <remarks>
        /// Runs before the zero-suppression early-return path too, so the guarantee holds in
        /// Release builds even though <see cref="NodeTable.Add"/> repeats part of this check.
        /// </remarks>
        private void ValidateKey(int level, int lo, int hi)
        {
            ThrowHelper.ThrowIfNegativeOrZero(level, nameof(level));

            int nextId = _nodes.NextId;

            if ((uint)lo >= (uint)nextId)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(lo),
                    $"The lo child must be an existing node id (0..{nextId - 1}), but was {lo}.");
            }

            if ((uint)hi >= (uint)nextId)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(hi),
                    $"The hi child must be an existing node id (0..{nextId - 1}), but was {hi}.");
            }
        }

        /// <summary>
        /// Debug-only assertion that a child's level is strictly below its parent's, catching
        /// variable-order violations at creation time rather than later as a corrupted canonical form.
        /// </summary>
        [Conditional("DEBUG")]
        private void AssertChildLevel(int level, int child, string name)
        {
            Debug.Assert(
                _nodes[child].Level < level,
                $"The {name} child (id {child}, level {_nodes[child].Level}) must sit strictly below level {level}.");
        }
    }
}

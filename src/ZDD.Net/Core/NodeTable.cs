using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Physical storage for ZDD nodes: a single growable array of <see cref="ZddNode"/>,
    /// indexed by node id (id == array index).
    /// </summary>
    /// <remarks>
    /// Terminals <see cref="Bottom"/> (0) and <see cref="Top"/> (1) occupy real slots so id ==
    /// index always holds. This type does not do uniquing or zero-suppression reduction; that's
    /// the unique table's job. Not thread-safe: concurrent <see cref="Add"/>/reads on one
    /// instance are unsafe, though bounds checks are never skipped, so the worst case of
    /// misuse is an exception or inconsistent read, not heap corruption.
    /// </remarks>
    internal sealed class NodeTable
    {
        /// <summary>Reserved id for terminal ⊥ (the empty family ∅).</summary>
        public const int Bottom = 0;

        /// <summary>Reserved id for terminal ⊤ (<c>{∅}</c>).</summary>
        public const int Top = 1;

        /// <summary>Id assigned to the first real node; also the count of reserved terminals.</summary>
        public const int FirstNodeId = 2;

        /// <summary>Sentinel meaning "no next" for <see cref="ZddNode.Next"/>.</summary>
        public const int NoNext = -1;

        /// <summary>
        /// Sentinel <see cref="Compact"/> uses, in the map it returns, for an id that did not
        /// survive collection (was not reachable from any mark root).
        /// </summary>
        public const int DeadId = -1;

        /// <summary>Default initial capacity (includes the 2 terminal slots).</summary>
        public const int DefaultCapacity = 1024;

        /// <summary>
        /// Upper bound on ids the table can allocate. Ids are <c>int</c>, but
        /// <see cref="Array.MaxLength"/> is reached first (~32 GB at 16 bytes/node).
        /// </summary>
        public static readonly int MaxCapacity = Array.MaxLength;

        /// <summary>
        /// Capacity ceiling; normally <see cref="MaxCapacity"/>. Overridable via the internal
        /// constructor so tests can exercise exhaustion without allocating 2^31 nodes.
        /// </summary>
        private readonly int _capacityLimit;

        private ZddNode[] _nodes;

        /// <summary>Slots used so far, including the 2 terminals; also the next id to hand out.</summary>
        private int _count;

        /// <summary>Highest value <see cref="_count"/> has ever reached.</summary>
        private int _peakCount;

        /// <summary>Creates a node table with the default initial capacity.</summary>
        public NodeTable()
            : this(DefaultCapacity, MaxCapacity)
        {
        }

        /// <summary>Creates a node table with the given initial capacity.</summary>
        /// <param name="initialCapacity">Initial capacity; must be at least <see cref="FirstNodeId"/>.</param>
        public NodeTable(int initialCapacity)
            : this(initialCapacity, MaxCapacity)
        {
        }

        /// <summary>Creates a node table with a given initial capacity and capacity limit (mainly for testing exhaustion).</summary>
        /// <param name="initialCapacity">Initial capacity; at least <see cref="FirstNodeId"/>.</param>
        /// <param name="capacityLimit">Capacity ceiling; between <paramref name="initialCapacity"/> and <see cref="MaxCapacity"/>.</param>
        public NodeTable(int initialCapacity, int capacityLimit)
        {
            if (initialCapacity < FirstNodeId)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    $"'{nameof(initialCapacity)}' must be at least {FirstNodeId} to hold the reserved terminals, but was {initialCapacity}.");
            }

            if (capacityLimit < initialCapacity || capacityLimit > MaxCapacity)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(capacityLimit),
                    $"'{nameof(capacityLimit)}' must be between {initialCapacity} and {MaxCapacity}, but was {capacityLimit}.");
            }

            _capacityLimit = capacityLimit;
            _nodes = GC.AllocateUninitializedArray<ZddNode>(initialCapacity);
            _count = FirstNodeId;
            _peakCount = FirstNodeId;

            // Terminals live on an uninitialized array, so write them explicitly.
            _nodes[Bottom] = new ZddNode { Level = 0, Lo = Bottom, Hi = Bottom, Next = NoNext };
            _nodes[Top] = new ZddNode { Level = 0, Lo = Bottom, Hi = Bottom, Next = NoNext };
        }

        /// <summary>Number of real nodes allocated (excludes the 2 reserved terminals).</summary>
        public int Count => _count - FirstNodeId;

        /// <summary>Highest value <see cref="Count"/> has ever reached.</summary>
        /// <remarks>
        /// Equal to <see cref="Count"/> until the first <see cref="ZddManager.Collect()"/>; a
        /// collection can shrink <see cref="Count"/> without lowering this, since it tracks the
        /// high-water mark rather than the current size.
        /// </remarks>
        public int PeakCount => _peakCount - FirstNodeId;

        /// <summary>Id the next <see cref="Add"/> call will return.</summary>
        public int NextId => _count;

        /// <summary>Current capacity (includes the 2 terminal slots).</summary>
        public int Capacity => _nodes.Length;

        /// <summary>Upper bound on ids this table can allocate.</summary>
        public int CapacityLimit => _capacityLimit;

        /// <summary>Whether an id is one of the reserved terminals (⊥ or ⊤).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsTerminal(int id) => (uint)id < FirstNodeId;

        /// <summary>Gets a reference to the node for an id; writes through it mutate the table.</summary>
        /// <remarks>A resize replaces the backing array, so don't hold a <c>ref</c> across a call to <see cref="Add"/>.</remarks>
        /// <param name="id">Node id, 0 up to (but excluding) <see cref="NextId"/>.</param>
        public ref ZddNode this[int id]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)id >= (uint)_count)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(id),
                        $"Node id {id} is out of range; the table currently holds ids 0..{_count - 1}.");
                }

                return ref _nodes[id];
            }
        }

        /// <summary>Adds a node and returns its id, growing the table first if full.</summary>
        /// <param name="level">Variable level; 1 or greater (0 is reserved for terminals).</param>
        /// <param name="lo">0-edge child id; must already exist.</param>
        /// <param name="hi">1-edge child id; must already exist and must not be <see cref="Bottom"/> (zero-suppression rule).</param>
        /// <exception cref="InvalidOperationException">The id space is exhausted.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Add(int level, int lo, int hi)
        {
            ValidateNewNode(level, lo, hi);

            int id = _count;

            if (id >= _nodes.Length)
            {
                Grow();
            }

            ref ZddNode node = ref _nodes[id];
            node.Level = level;
            node.Lo = lo;
            node.Hi = hi;
            node.Next = NoNext;

            _count = id + 1;

            if (_count > _peakCount)
            {
                _peakCount = _count;
            }

            return id;
        }

        private void ValidateNewNode(int level, int lo, int hi)
        {
            ThrowHelper.ThrowIfNegativeOrZero(level, nameof(level));

            if ((uint)lo >= (uint)_count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(lo),
                    $"The lo child must be an existing node id (0..{_count - 1}), but was {lo}.");
            }

            if ((uint)hi >= (uint)_count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(hi),
                    $"The hi child must be an existing node id (0..{_count - 1}), but was {hi}.");
            }

            if (hi == Bottom)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(hi),
                    "The hi child must not be the bottom terminal: a node whose 1-edge points to bottom is removed by the zero-suppressed reduction rule.");
            }
        }

        /// <summary>Doubles capacity (or grows to the limit if closer). Uses uninitialized allocation + copy, since only ids below <see cref="NextId"/> are ever read.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Grow()
        {
            int capacity = _nodes.Length;
            if (capacity >= _capacityLimit)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The node table has run out of ids: its limit of {_capacityLimit} ids " +
                    $"(including the {FirstNodeId} reserved terminals) is exhausted. " +
                    "Node ids are 32bit by design (docs/PLAN.md §4.1), so the diagram cannot grow any further.");
            }

            int newCapacity = capacity <= _capacityLimit / 2 ? capacity * 2 : _capacityLimit;

            ZddNode[] grown = GC.AllocateUninitializedArray<ZddNode>(newCapacity);
            Array.Copy(_nodes, grown, capacity);
            _nodes = grown;
        }

        /// <summary>
        /// Compacts the table in place, keeping only the nodes marked in <paramref name="live"/>
        /// (terminals are always kept regardless of <paramref name="live"/>) and renumbering the
        /// survivors densely from <see cref="FirstNodeId"/>, in their original relative order.
        /// </summary>
        /// <param name="live">
        /// Liveness by id, indexed 0 .. <see cref="NextId"/> - 1 (entries for <see cref="Bottom"/>/<see cref="Top"/> are ignored).
        /// </param>
        /// <returns>
        /// Old id -&gt; new id map, length <see cref="NextId"/> as of this call. Terminals map to
        /// themselves; a dead (non-terminal, not live) id maps to <see cref="DeadId"/>.
        /// </returns>
        /// <remarks>
        /// A node's <see cref="ZddNode.Lo"/>/<see cref="ZddNode.Hi"/> always have a strictly smaller
        /// id than the node itself: the unique table requires both children to already exist before
        /// a new node can reference them, so a child is always added first. Walking old ids in
        /// increasing order therefore guarantees every live child's new id is already known by the
        /// time its parent is remapped, so this single forward pass can safely write each surviving
        /// node to its new (never larger) slot without a second array: <c>newId &lt;= oldId</c>
        /// always holds, so a slot is never overwritten before it has been read.
        /// </remarks>
        internal int[] Compact(ReadOnlySpan<bool> live)
        {
            int oldCount = _count;
            int[] map = new int[oldCount];
            map[Bottom] = Bottom;
            map[Top] = Top;

            int writeId = FirstNodeId;
            for (int oldId = FirstNodeId; oldId < oldCount; oldId++)
            {
                if (!live[oldId])
                {
                    map[oldId] = DeadId;
                    continue;
                }

                int level;
                int lo;
                int hi;
                {
                    ref ZddNode src = ref _nodes[oldId];
                    level = src.Level;
                    lo = src.Lo;
                    hi = src.Hi;
                }

                map[oldId] = writeId;

                ref ZddNode dst = ref _nodes[writeId];
                dst.Level = level;
                dst.Lo = IsTerminal(lo) ? lo : map[lo];
                dst.Hi = IsTerminal(hi) ? hi : map[hi];
                dst.Next = NoNext;

                writeId++;
            }

            _count = writeId;
            ShrinkToFit();

            return map;
        }

        /// <summary>
        /// Reallocates the backing array to the smallest power-of-two capacity (at least
        /// <see cref="DefaultCapacity"/>) that fits the current <see cref="Count"/>, if that is
        /// meaningfully smaller than what is currently allocated. Called after <see cref="Compact"/>
        /// so a collection that frees most of the table actually returns the memory.
        /// </summary>
        private void ShrinkToFit()
        {
            int currentCapacity = _nodes.Length;

            long desired = Math.Max(DefaultCapacity, (long)BitOperations.RoundUpToPowerOf2((uint)Math.Max(_count, 1)));
            desired = Math.Min(desired, (long)_capacityLimit);

            // Only worth reallocating (and copying) when it actually halves usage or more.
            if (desired > currentCapacity / 2)
            {
                return;
            }

            ZddNode[] shrunk = GC.AllocateUninitializedArray<ZddNode>((int)desired);
            Array.Copy(_nodes, shrunk, _count);
            _nodes = shrunk;
        }
    }
}

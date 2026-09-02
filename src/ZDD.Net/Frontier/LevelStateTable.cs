using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// Shared plumbing of the per-level state tables: the open-addressing slot array, the growth
    /// policy, and the statistics. Subclasses own the state storage and the probe loop, since
    /// comparing states is what has to stay inlined.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Frontier search collapses two branches that reach the same state into one, and that merge
    /// is the only reason the search does not blow up exponentially. A table holds one level, so
    /// it is filled, read once, and then <see cref="Clear"/>ed for the next level; buffers come
    /// from <see cref="ArrayPool{T}"/>, so a build holds at most two levels no matter how deep it
    /// goes. Not thread-safe.
    /// </para>
    /// <para>
    /// Slots hold an entry index plus one so that zero can mark an empty slot, and the entry's
    /// hash is cached alongside its state: it rejects most probe mismatches without calling the
    /// spec, and it makes a grow a pure rehash.
    /// </para>
    /// </remarks>
    internal abstract class LevelStateTable : IDisposable
    {
        /// <summary>Load factor (%) at which the slot array doubles; matches the unique table.</summary>
        public const int MaxLoadFactorPercent = 70;

        /// <summary>Minimum slot count (power of two).</summary>
        public const int MinimumCapacity = 4;

        /// <summary>Default initial slot count (power of two).</summary>
        public const int DefaultCapacity = 1024;

        /// <summary>Maximum slot count: the largest power of two that fits in an array.</summary>
        public const int MaxCapacity = 1 << 30;

        /// <summary>Sentinel marking an empty slot; an occupied slot holds the entry index plus one.</summary>
        private protected const int EmptySlot = 0;

        /// <summary>Slot to entry index plus one; only the first <see cref="Capacity"/> elements are ours.</summary>
        private protected int[] _slots;

        /// <summary>Cached hash of each entry, indexed in parallel with the state storage.</summary>
        private protected int[] _hashes;

        /// <summary>Slot count in use; a power of two, which may be below <c>_slots.Length</c> after pooling.</summary>
        private protected int _capacity;

        /// <summary>Entries registered for the current level.</summary>
        private protected int _count;

        /// <summary>Entry count above which the next registration grows the table.</summary>
        private protected int _growThreshold;

        private protected long _collisions;
        private protected long _totalRegistered;
        private protected int _peakWidth;
        private protected bool _disposed;

        /// <summary>Creates a table sized for <paramref name="initialCapacity"/> slots.</summary>
        /// <param name="initialCapacity">Initial slot count, rounded up to a power of two.</param>
        private protected LevelStateTable(int initialCapacity)
        {
            ThrowHelper.ThrowIfNegativeOrZero(initialCapacity, nameof(initialCapacity));

            if (initialCapacity > MaxCapacity)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    $"'{nameof(initialCapacity)}' must not exceed {MaxCapacity}, but was {initialCapacity}.");
            }

            int capacity = Math.Max(MinimumCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)initialCapacity));

            _capacity = capacity;
            _slots = RentCleared(capacity);
            _hashes = ArrayPool<int>.Shared.Rent(capacity);
            _growThreshold = ComputeGrowThreshold(capacity);
        }

        /// <summary>Entries registered for the current level; also the next index <c>GetOrAdd</c> hands out.</summary>
        public int Count => _count;

        /// <summary>Current slot count (power of two).</summary>
        public int Capacity => _capacity;

        /// <summary>Entry count above which the next registration triggers a grow.</summary>
        public int GrowThreshold => _growThreshold;

        /// <summary>Occupied slots skipped over during linear probes, over the table's whole life.</summary>
        /// <remarks>Rehashing during a grow is not counted; neither <see cref="Clear"/> nor a grow resets this.</remarks>
        public long Collisions => _collisions;

        /// <summary>Entries registered over every level this table has held.</summary>
        public long TotalRegistered => _totalRegistered;

        /// <summary>The largest <see cref="Count"/> any single level reached: the peak frontier width.</summary>
        public int PeakWidth => _peakWidth;

        /// <summary>Drops every entry, keeping the buffers so the next level reuses them.</summary>
        public virtual void Clear()
        {
            ThrowIfDisposed();

            Array.Clear(_slots, 0, _capacity);
            _count = 0;
        }

        /// <summary>Returns the pooled buffers. The table must not be used afterwards.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReturnStates();
            ArrayPool<int>.Shared.Return(_slots);
            ArrayPool<int>.Shared.Return(_hashes);
            _slots = Array.Empty<int>();
            _hashes = Array.Empty<int>();
            _capacity = 0;
            _count = 0;
            _growThreshold = 0;
        }

        /// <summary>Rents a zeroed <see cref="int"/> array; pooled arrays come back with stale contents.</summary>
        private protected static int[] RentCleared(int length)
        {
            int[] rented = ArrayPool<int>.Shared.Rent(length);
            Array.Clear(rented, 0, length);
            return rented;
        }

        private protected static int ComputeGrowThreshold(int capacity) =>
            (int)((long)capacity * MaxLoadFactorPercent / 100);

        /// <summary>The slot a hash starts probing at.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected int SlotFor(int hash) =>
            Hashing.IndexForPowerOfTwo(Hashing.Combine(hash), _capacity);

        /// <summary>
        /// Finds the empty slot a hash belongs in. Only valid for a state known not to be
        /// registered yet, since no state comparison is performed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected int FindEmptySlot(int hash)
        {
            int[] slots = _slots;
            int mask = _capacity - 1;
            int slot = SlotFor(hash);

            while (slots[slot] != EmptySlot)
            {
                slot = (slot + 1) & mask;
            }

            return slot;
        }

        /// <summary>Records a freshly registered entry and its hash; the caller stores the state itself.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected void Register(int index, int slot, int hash)
        {
            _hashes[index] = hash;
            _slots[slot] = index + 1;
            _count = index + 1;
            _totalRegistered++;

            if (_count > _peakWidth)
            {
                _peakWidth = _count;
            }
        }

        /// <summary>Doubles the capacity and rehashes; entry indices, and so the ones already handed out, are preserved.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private protected void Grow()
        {
            int capacity = _capacity;

            if (capacity >= MaxCapacity)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"The level state table cannot grow beyond {MaxCapacity} slots, which is the largest power " +
                    $"of two that fits in an array. It currently holds {_count} state(s).");
            }

            int newCapacity = capacity * 2;

            GrowStates(newCapacity);
            GrowHashes(newCapacity);

            int[] grown = ArrayPool<int>.Shared.Rent(newCapacity);
            ArrayPool<int>.Shared.Return(_slots);
            _slots = grown;
            _capacity = newCapacity;
            _growThreshold = ComputeGrowThreshold(newCapacity);
            RehashSlots();
        }

        /// <summary>
        /// Rebuilds the slot array from the cached hashes, keeping every entry index. Needed after a
        /// grow, and after a re-encoding that changes what the entries hash to.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private protected void RehashSlots()
        {
            int[] slots = _slots;
            int mask = _capacity - 1;

            Array.Clear(slots, 0, _capacity);

            for (int index = 0; index < _count; index++)
            {
                int slot = SlotFor(_hashes[index]);
                while (slots[slot] != EmptySlot)
                {
                    slot = (slot + 1) & mask;
                }

                slots[slot] = index + 1;
            }
        }

        /// <summary>Grows the state storage to hold <paramref name="newCapacity"/> entries, keeping the existing ones.</summary>
        private protected abstract void GrowStates(int newCapacity);

        /// <summary>Returns the state storage to its pool.</summary>
        private protected abstract void ReturnStates();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private protected void ThrowIfDisposed()
        {
            if (_disposed)
            {
                ThrowHelper.ThrowObjectDisposedException(GetType().Name);
            }
        }

        private void GrowHashes(int newCapacity)
        {
            int[] grown = ArrayPool<int>.Shared.Rent(newCapacity);
            Array.Copy(_hashes, grown, _count);
            ArrayPool<int>.Shared.Return(_hashes);
            _hashes = grown;
        }
    }
}

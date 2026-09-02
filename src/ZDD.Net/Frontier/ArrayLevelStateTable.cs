using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The level state table for the variable-length states of <see cref="IArrayDdSpec"/>: every
    /// state is <see cref="ArrayLength"/> slots, bit-packed by a shared <see cref="PackedStateLayout"/>
    /// and stored inline in one flat byte array at a fixed stride.
    /// </summary>
    /// <remarks>
    /// One flat array rather than an array of arrays: the states of a level are walked in index
    /// order, and a per-state array would add an object header, an indirection, and a GC reference
    /// each. Packing usually cuts a slot from four bytes to one (M3-2); comparison and hashing then
    /// run over the packed bytes, word at a time, and never consult the spec — equality is
    /// element-wise, as <see cref="IArrayDdSpec"/> specifies, and packing is injective.
    /// </remarks>
    internal sealed class ArrayLevelStateTable : LevelStateTable
    {
        private readonly int _arrayLength;
        private readonly PackedStateLayout _layout;

        /// <summary>Packed states end to end; entry <c>i</c> starts at <c>i * _stride</c>.</summary>
        private byte[] _states;

        /// <summary>Scratch holding the state being looked up, packed; <see cref="_stride"/> bytes.</summary>
        private byte[] _packed;

        /// <summary>The layout <see cref="_states"/> is written under, and its stride in bytes.</summary>
        private int _bias;
        private int _bytesPerSlot;
        private int _stride;
        private int _layoutVersion;

        /// <summary>Creates a table for states of <paramref name="arrayLength"/> slots, with the default capacity.</summary>
        public ArrayLevelStateTable(int arrayLength)
            : this(arrayLength, DefaultCapacity, new PackedStateLayout())
        {
        }

        /// <summary>Creates a table for states of <paramref name="arrayLength"/> slots, with its own layout.</summary>
        public ArrayLevelStateTable(int arrayLength, int initialCapacity)
            : this(arrayLength, initialCapacity, new PackedStateLayout())
        {
        }

        /// <summary>Creates a table for states of <paramref name="arrayLength"/> slots.</summary>
        /// <param name="arrayLength">The spec's <see cref="IArrayDdSpec.ArrayLength"/>; every state must match it.</param>
        /// <param name="initialCapacity">Initial slot count, rounded up to a power of two.</param>
        /// <param name="layout">How states are packed; share one instance across the levels of a build.</param>
        public ArrayLevelStateTable(int arrayLength, int initialCapacity, PackedStateLayout layout)
            : base(initialCapacity)
        {
            ThrowHelper.ThrowIfNegativeOrZero(arrayLength, nameof(arrayLength));
            ThrowHelper.ThrowIfNull(layout, nameof(layout));

            _arrayLength = arrayLength;
            _layout = layout;
            _bias = layout.Bias;
            _bytesPerSlot = layout.BytesPerSlot;
            _layoutVersion = layout.Version;
            _stride = layout.StrideFor(arrayLength);
            _states = ArrayPool<byte>.Shared.Rent(StateBufferLength(_capacity, _stride));
            _packed = ArrayPool<byte>.Shared.Rent(_stride);
        }

        /// <summary>The number of slots in every state held here.</summary>
        public int ArrayLength => _arrayLength;

        /// <summary>Bytes one slot currently occupies: 1, 2 or 4.</summary>
        public int BytesPerSlot => _bytesPerSlot;

        /// <summary>Writes the state registered under <paramref name="index"/> into <paramref name="destination"/>.</summary>
        /// <param name="index">An index previously returned by <see cref="GetOrAdd"/> for the current level.</param>
        /// <param name="destination">Receives the unpacked state; exactly <see cref="ArrayLength"/> slots.</param>
        public void CopyStateTo(int index, Span<int> destination)
        {
            if ((uint)index >= (uint)_count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(index),
                    $"'{nameof(index)}' must be an index of this level (0..{_count - 1}), but was {index}.");
            }

            if (destination.Length != _arrayLength)
            {
                ThrowHelper.ThrowArgumentException(
                    nameof(destination),
                    $"'{nameof(destination)}' must have exactly {_arrayLength} slot(s), but had {destination.Length}.");
            }

            PackedStateLayout.Unpack(PackedAt(index), destination, _bias, _bytesPerSlot);
        }

        /// <summary>Returns the index of <paramref name="state"/> in this level, registering it if new.</summary>
        /// <param name="state">The state to look up; exactly <see cref="ArrayLength"/> slots, copied on registration.</param>
        /// <returns>The level-local index, stable until <see cref="LevelStateTable.Clear"/>.</returns>
        public int GetOrAdd(ReadOnlySpan<int> state)
        {
            ThrowIfDisposed();

            if (state.Length != _arrayLength)
            {
                ThrowHelper.ThrowArgumentException(
                    nameof(state),
                    $"A state must have exactly {_arrayLength} slot(s), as the spec's ArrayLength says, but had {state.Length}.");
            }

            if (_layoutVersion != _layout.Version)
            {
                Reencode();
            }

            if (!_layout.TryPack(state, _packed.AsSpan(0, _stride)))
            {
                // A value outside the current window: widen the shared layout, rewrite what is
                // already here, and pack again — the widened layout holds this state by construction.
                _layout.Extend(state);
                Reencode();

                bool packed = _layout.TryPack(state, _packed.AsSpan(0, _stride));
                Debug.Assert(packed, "A layout extended for a state must be able to pack that state.");
            }

            ReadOnlySpan<byte> key = _packed.AsSpan(0, _stride);
            int hash = (int)Hashing.Combine(key);
            int[] slots = _slots;
            int mask = _capacity - 1;
            int slot = SlotFor(hash);

            while (true)
            {
                int entry = slots[slot];
                if (entry == EmptySlot)
                {
                    break;
                }

                int index = entry - 1;
                if (_hashes[index] == hash && key.SequenceEqual(PackedAt(index)))
                {
                    return index;
                }

                _collisions++;
                slot = (slot + 1) & mask;
            }

            // Reached an empty slot, so the state is new. Growing moves it, so re-probe afterwards.
            if (_count + 1 > _growThreshold)
            {
                Grow();
                slot = FindEmptySlot(hash);
            }

            int newIndex = _count;
            key.CopyTo(_states.AsSpan(newIndex * _stride, _stride));
            Register(newIndex, slot, hash);
            return newIndex;
        }

        /// <inheritdoc/>
        private protected override void GrowStates(int newCapacity)
        {
            byte[] grown = ArrayPool<byte>.Shared.Rent(StateBufferLength(newCapacity, _stride));
            Array.Copy(_states, grown, _count * _stride);
            ArrayPool<byte>.Shared.Return(_states);
            _states = grown;
        }

        /// <inheritdoc/>
        private protected override void ReturnStates()
        {
            ArrayPool<byte>.Shared.Return(_states);
            ArrayPool<byte>.Shared.Return(_packed);
            _states = Array.Empty<byte>();
            _packed = Array.Empty<byte>();
        }

        /// <summary>The packed state buffer length, rejecting the sizes an array cannot hold.</summary>
        private static int StateBufferLength(int capacity, int stride)
        {
            long length = (long)capacity * stride;

            if (length > Array.MaxLength)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"A level of {capacity} state(s) of {stride} byte(s) each needs {length} byte(s), " +
                    $"which exceeds the largest array .NET can allocate ({Array.MaxLength}).");
            }

            return (int)length;
        }

        private ReadOnlySpan<byte> PackedAt(int index) => _states.AsSpan(index * _stride, _stride);

        /// <summary>
        /// Rewrites every entry under the shared layout's current encoding, keeping entry indices —
        /// the hashes change with the bytes, so the slot array is rebuilt from them.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Reencode()
        {
            int bias = _layout.Bias;
            int bytesPerSlot = _layout.BytesPerSlot;
            int stride = _layout.StrideFor(_arrayLength);
            byte[] repacked = ArrayPool<byte>.Shared.Rent(StateBufferLength(_capacity, stride));
            int[] state = ArrayPool<int>.Shared.Rent(_arrayLength);

            try
            {
                Span<int> slotValues = state.AsSpan(0, _arrayLength);

                for (int index = 0; index < _count; index++)
                {
                    Span<byte> destination = repacked.AsSpan(index * stride, stride);

                    PackedStateLayout.Unpack(PackedAt(index), slotValues, _bias, _bytesPerSlot);
                    bool packed = PackedStateLayout.TryPack(slotValues, destination, bias, bytesPerSlot);
                    Debug.Assert(packed, "Every state already registered must fit the widened layout.");

                    _hashes[index] = (int)Hashing.Combine((ReadOnlySpan<byte>)destination);
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(state);
            }

            ArrayPool<byte>.Shared.Return(_states);
            ArrayPool<byte>.Shared.Return(_packed);
            _states = repacked;
            _packed = ArrayPool<byte>.Shared.Rent(stride);
            _bias = bias;
            _bytesPerSlot = bytesPerSlot;
            _stride = stride;
            _layoutVersion = _layout.Version;
            RehashSlots();
        }
    }
}

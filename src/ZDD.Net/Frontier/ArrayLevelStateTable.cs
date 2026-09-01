using System;
using System.Buffers;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The level state table for the variable-length states of <see cref="IArrayDdSpec"/>: every
    /// state is <see cref="ArrayLength"/> <see cref="int"/>s, packed end to end in a single array
    /// and addressed by offset.
    /// </summary>
    /// <remarks>
    /// One flat array rather than an array of arrays: the states of a level are walked in index
    /// order, and a per-state array would add an object header, an indirection, and a GC reference
    /// each. Equality is element-wise, as <see cref="IArrayDdSpec"/> specifies, so the spec is not
    /// consulted here at all.
    /// </remarks>
    internal sealed class ArrayLevelStateTable : LevelStateTable
    {
        private readonly int _arrayLength;

        /// <summary>States packed <see cref="_arrayLength"/> at a time; entry <c>i</c> starts at <c>i * _arrayLength</c>.</summary>
        private int[] _states;

        /// <summary>Creates a table for states of <paramref name="arrayLength"/> slots, with the default capacity.</summary>
        public ArrayLevelStateTable(int arrayLength)
            : this(arrayLength, DefaultCapacity)
        {
        }

        /// <summary>Creates a table for states of <paramref name="arrayLength"/> slots.</summary>
        /// <param name="arrayLength">The spec's <see cref="IArrayDdSpec.ArrayLength"/>; every state must match it.</param>
        /// <param name="initialCapacity">Initial slot count, rounded up to a power of two.</param>
        public ArrayLevelStateTable(int arrayLength, int initialCapacity)
            : base(initialCapacity)
        {
            ThrowHelper.ThrowIfNegativeOrZero(arrayLength, nameof(arrayLength));

            _arrayLength = arrayLength;
            _states = ArrayPool<int>.Shared.Rent(StateBufferLength(_capacity, arrayLength));
        }

        /// <summary>The number of <see cref="int"/> slots in every state held here.</summary>
        public int ArrayLength => _arrayLength;

        /// <summary>The state registered under <paramref name="index"/>.</summary>
        /// <param name="index">An index previously returned by <see cref="GetOrAdd"/> for the current level.</param>
        public ReadOnlySpan<int> this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(index),
                        $"'{nameof(index)}' must be an index of this level (0..{_count - 1}), but was {index}.");
                }

                return _states.AsSpan(index * _arrayLength, _arrayLength);
            }
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

            int hash = (int)Hashing.Combine(state);
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
                if (_hashes[index] == hash && state.SequenceEqual(StateAt(index)))
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
            state.CopyTo(_states.AsSpan(newIndex * _arrayLength, _arrayLength));
            Register(newIndex, slot, hash);
            return newIndex;
        }

        /// <inheritdoc/>
        private protected override void GrowStates(int newCapacity)
        {
            int[] grown = ArrayPool<int>.Shared.Rent(StateBufferLength(newCapacity, _arrayLength));
            Array.Copy(_states, grown, _count * _arrayLength);
            ArrayPool<int>.Shared.Return(_states);
            _states = grown;
        }

        /// <inheritdoc/>
        private protected override void ReturnStates()
        {
            ArrayPool<int>.Shared.Return(_states);
            _states = Array.Empty<int>();
        }

        /// <summary>The packed state buffer length, rejecting the sizes an array cannot hold.</summary>
        private static int StateBufferLength(int capacity, int arrayLength)
        {
            long length = (long)capacity * arrayLength;

            if (length > Array.MaxLength)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    $"A level of {capacity} state(s) of {arrayLength} slot(s) each needs {length} int(s), " +
                    $"which exceeds the largest array .NET can allocate ({Array.MaxLength}).");
            }

            return (int)length;
        }

        private ReadOnlySpan<int> StateAt(int index) => _states.AsSpan(index * _arrayLength, _arrayLength);
    }
}

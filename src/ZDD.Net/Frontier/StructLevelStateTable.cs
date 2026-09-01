using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The level state table for a fixed-size <c>struct</c> state: states are stored inline in one
    /// array and deduplicated through the spec's <see cref="IDdSpec{TState}.StateHashCode"/> and
    /// <see cref="IDdSpec{TState}.StateEquals"/>.
    /// </summary>
    /// <typeparam name="TSpec">
    /// The spec, taken as a type parameter so its calls are devirtualized and inlined; an
    /// interface-typed field would make every probe a virtual call (docs/PLAN.md §10-2).
    /// </typeparam>
    /// <typeparam name="TState">The state carried between levels.</typeparam>
    /// <remarks>
    /// Write the spec as a <c>readonly struct</c>: the spec is held in a readonly field, so a
    /// mutable one is defensively copied on every call.
    /// </remarks>
    internal sealed class StructLevelStateTable<TSpec, TState> : LevelStateTable
        where TSpec : struct, IDdSpec<TState>
    {
        private readonly TSpec _spec;

        /// <summary>States by entry index; only the first <see cref="LevelStateTable.Count"/> are live.</summary>
        private TState[] _states;

        /// <summary>Creates a table for <paramref name="spec"/> with the default initial capacity.</summary>
        public StructLevelStateTable(TSpec spec)
            : this(spec, DefaultCapacity)
        {
        }

        /// <summary>Creates a table for <paramref name="spec"/>.</summary>
        /// <param name="spec">The spec whose equality and hash decide which states merge.</param>
        /// <param name="initialCapacity">Initial slot count, rounded up to a power of two.</param>
        public StructLevelStateTable(TSpec spec, int initialCapacity)
            : base(initialCapacity)
        {
            _spec = spec;
            _states = ArrayPool<TState>.Shared.Rent(_capacity);
        }

        /// <summary>The state registered under <paramref name="index"/>.</summary>
        /// <param name="index">An index previously returned by <see cref="GetOrAdd"/> for the current level.</param>
        public ref readonly TState this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    ThrowHelper.ThrowArgumentOutOfRangeException(
                        nameof(index),
                        $"'{nameof(index)}' must be an index of this level (0..{_count - 1}), but was {index}.");
                }

                return ref _states[index];
            }
        }

        /// <summary>Returns the index of <paramref name="state"/> in this level, registering it if new.</summary>
        /// <param name="state">The state to look up; it is copied on registration.</param>
        /// <returns>The level-local index, stable until <see cref="LevelStateTable.Clear"/>.</returns>
        public int GetOrAdd(in TState state)
        {
            ThrowIfDisposed();

            int hash = _spec.StateHashCode(state);
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
                if (_hashes[index] == hash && _spec.StateEquals(_states[index], state))
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
            _states[newIndex] = state;
            Register(newIndex, slot, hash);
            return newIndex;
        }

        /// <inheritdoc/>
        public override void Clear()
        {
            // Only states that reference the heap need wiping; leaving them would pin whatever
            // the finished level referred to for as long as the table lives.
            if (RuntimeHelpers.IsReferenceOrContainsReferences<TState>())
            {
                Array.Clear(_states, 0, _count);
            }

            base.Clear();
        }

        /// <inheritdoc/>
        private protected override void GrowStates(int newCapacity)
        {
            TState[] grown = ArrayPool<TState>.Shared.Rent(newCapacity);
            Array.Copy(_states, grown, _count);
            ReturnStateBuffer();
            _states = grown;
        }

        /// <inheritdoc/>
        private protected override void ReturnStates()
        {
            ReturnStateBuffer();
            _states = Array.Empty<TState>();
        }

        private void ReturnStateBuffer() =>
            ArrayPool<TState>.Shared.Return(_states, RuntimeHelpers.IsReferenceOrContainsReferences<TState>());
    }
}

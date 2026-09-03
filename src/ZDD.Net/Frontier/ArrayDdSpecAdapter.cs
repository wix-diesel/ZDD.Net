using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// Wraps an <see cref="IArrayDdSpec"/> as an <see cref="IDdSpec{TState}"/> over <c>int[]</c>, so it can
    /// take part in <see cref="AndSpec{TSpec1, TState1, TSpec2, TState2}"/> / <see cref="OrSpec{TSpec1, TState1, TSpec2, TState2}"/>
    /// composition alongside (or with) a scalar-state spec. Built by the array-taking overloads of
    /// <see cref="DdSpecExtensions"/>'s <c>And</c> / <c>Or</c>; not normally constructed directly.
    /// </summary>
    /// <typeparam name="TSpec">The wrapped spec's type.</typeparam>
    /// <remarks>
    /// <see cref="IDdSpec{TState}"/> requires a state that "must not be mutated in place" when
    /// <c>TState</c> is a reference type (<c>int[]</c> here), since only the reference is
    /// copied between branches — unlike <see cref="IArrayDdSpec"/>, whose <c>Span&lt;int&gt;</c> contract
    /// is the opposite (mutate the given buffer in place). This adapter bridges the two by allocating a
    /// fresh array for every <see cref="GetRoot"/> / <see cref="GetChild"/> call and reassigning the
    /// <c>ref</c> state to it, rather than writing into the array it was given — the one deliberate
    /// allocation this composition layer makes, and only on the array-wrapped side.
    /// </remarks>
    public readonly struct ArrayDdSpecAdapter<TSpec> : IDdSpec<int[]>
        where TSpec : struct, IArrayDdSpec
    {
        private readonly TSpec _spec;

        /// <summary>Wraps <paramref name="spec"/>.</summary>
        /// <param name="spec">The array-state spec to wrap.</param>
        public ArrayDdSpecAdapter(TSpec spec)
        {
            _spec = spec;
        }

        /// <inheritdoc/>
        public int GetRoot(ref int[] state)
        {
            int[] array = new int[_spec.ArrayLength];
            int level = _spec.GetRoot(array);
            state = array;
            return level;
        }

        /// <inheritdoc/>
        public int GetChild(ref int[] state, int level, int value)
        {
            int[] child = new int[state.Length];
            state.CopyTo(child.AsSpan());
            int childLevel = _spec.GetChild(child, level, value);
            state = child;
            return childLevel;
        }

        /// <inheritdoc/>
        public bool StateEquals(in int[] left, in int[] right)
        {
            if (left is null || right is null)
            {
                return left is null && right is null;
            }

            return left.AsSpan().SequenceEqual(right);
        }

        /// <inheritdoc/>
        public int StateHashCode(in int[] state)
        {
            if (state is null)
            {
                return 0;
            }

            HashCode hash = default;
            foreach (int value in state)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}

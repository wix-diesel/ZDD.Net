using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// Adapts an <see cref="IArrayDdSpec"/> into an <see cref="IDdSpec{TState}"/> over <c>int[]</c>, so
    /// it can compose (<see cref="SpecExtensions.And"/> / <see cref="SpecExtensions.Or"/>) with a spec
    /// of a different kind — for instance <c>PathSpec</c> (array-state) <c>.And</c> <c>CardinalitySpec</c>
    /// (fixed-state).
    /// </summary>
    /// <typeparam name="TSpec">The array-state spec being adapted.</typeparam>
    /// <remarks>
    /// <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/> / <see cref="OrSpec{TSpecA, TStateA, TSpecB, TStateB}"/>
    /// copy a branch's state with a plain field copy (docs/frontier-spec-guide.md §4), which is only
    /// safe for value types. <c>int[]</c> is a reference type, so that copy would alias the same array
    /// across sibling branches; this adapter defends against it by cloning the array on every
    /// <see cref="GetChild"/> call before handing it to <typeparamref name="TSpec"/>. That trades one
    /// allocation per state/branch for correctness — a real cost, but still dwarfed by the intermediate
    /// diagram a post-filter (build, then <c>Intersect</c>) would otherwise materialize (docs/PLAN.md §6.3).
    /// A spec written directly against <see cref="IDdSpec{TState}"/> with a genuine struct state pays
    /// none of this, so prefer composing those directly when both sides support it.
    /// </remarks>
    public readonly struct ArrayDdSpecAdapter<TSpec> : IDdSpec<int[]>
        where TSpec : struct, IArrayDdSpec
    {
        private readonly TSpec _spec;

        /// <summary>Wraps <paramref name="spec"/> for composition.</summary>
        public ArrayDdSpecAdapter(TSpec spec) => _spec = spec;

        /// <inheritdoc/>
        public int GetRoot(ref int[] state)
        {
            int[] array = new int[_spec.ArrayLength];
            int result = _spec.GetRoot(array);
            state = array;
            return result;
        }

        /// <inheritdoc/>
        public int GetChild(ref int[] state, int level, int value)
        {
            int[] clone = (int[])state.Clone();
            int result = _spec.GetChild(clone, level, value);
            state = clone;
            return result;
        }

        /// <inheritdoc/>
        public bool StateEquals(in int[] left, in int[] right) => left.AsSpan().SequenceEqual(right);

        /// <inheritdoc/>
        public int StateHashCode(in int[] state)
        {
            HashCode hash = default;
            foreach (int slot in state)
            {
                hash.Add(slot);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>Extension point for adapting an <see cref="IArrayDdSpec"/> into a composable <see cref="IDdSpec{TState}"/>.</summary>
    public static class ArrayDdSpecAdapterExtensions
    {
        /// <summary>Wraps <paramref name="spec"/> as an <see cref="IDdSpec{TState}"/> so it can be composed via <see cref="SpecExtensions"/>.</summary>
        public static ArrayDdSpecAdapter<TSpec> AsDdSpec<TSpec>(this TSpec spec)
            where TSpec : struct, IArrayDdSpec
        {
            return new ArrayDdSpecAdapter<TSpec>(spec);
        }
    }
}

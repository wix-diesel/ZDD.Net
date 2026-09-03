using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The intersection of two specs: the family of sets both <typeparamref name="TSpec1"/> and
    /// <typeparamref name="TSpec2"/> accept, built directly — without ever materializing either
    /// spec's own family in full (docs/PLAN.md &#167;6.3). Built by <see cref="DdSpecExtensions"/>'s <c>And</c>.
    /// </summary>
    /// <typeparam name="TSpec1">The first spec's type.</typeparam>
    /// <typeparam name="TState1">The first spec's state.</typeparam>
    /// <typeparam name="TSpec2">The second spec's type.</typeparam>
    /// <typeparam name="TState2">The second spec's state.</typeparam>
    /// <remarks>
    /// <para>
    /// Both components are stepped at every composed level (see <see cref="CompositionStep"/> for how a
    /// component that skipped ahead, or already resolved, is kept "not due" without re-invoking it).
    /// &#8869; from either component makes the whole composed branch &#8869; immediately: once one spec
    /// rejects, nothing the other says can matter. &#8868; needs <i>both</i> components at once — a lone
    /// &#8868; only means "this one requires the rest excluded", so it is carried forward and keeps
    /// forcing exclusion (any later inclusion collapses it to &#8869;, which then collapses the whole
    /// composition) until either the other component resolves too or the two together run out of levels.
    /// </para>
    /// <para>
    /// This struct is itself an <see cref="IDdSpec{TState}"/>, so <c>a.And(b).And(c)</c> composes to
    /// arbitrary depth without any special-casing.
    /// </para>
    /// </remarks>
    public readonly struct AndSpec<TSpec1, TState1, TSpec2, TState2> : IDdSpec<PairState<TState1, TState2>>
        where TSpec1 : struct, IDdSpec<TState1>
        where TSpec2 : struct, IDdSpec<TState2>
    {
        private readonly TSpec1 _spec1;
        private readonly TSpec2 _spec2;

        /// <summary>Creates the intersection of <paramref name="spec1"/> and <paramref name="spec2"/>.</summary>
        /// <param name="spec1">The first spec.</param>
        /// <param name="spec2">The second spec.</param>
        public AndSpec(TSpec1 spec1, TSpec2 spec2)
        {
            _spec1 = spec1;
            _spec2 = spec2;
        }

        /// <inheritdoc/>
        public int GetRoot(ref PairState<TState1, TState2> state)
        {
            int level1 = _spec1.GetRoot(ref state.State1);
            int level2 = _spec2.GetRoot(ref state.State2);

            if (level1 == DdResult.False || level2 == DdResult.False)
            {
                return DdResult.False;
            }

            if (level1 == DdResult.True)
            {
                state.State1 = default!;
            }

            if (level2 == DdResult.True)
            {
                state.State2 = default!;
            }

            if (level1 == DdResult.True && level2 == DdResult.True)
            {
                return DdResult.True;
            }

            state.Level1 = level1;
            state.Level2 = level2;
            return Math.Max(level1, level2);
        }

        /// <inheritdoc/>
        public int GetChild(ref PairState<TState1, TState2> state, int level, int value)
        {
            int newLevel1 = CompositionStep.Step(in _spec1, state.Level1, ref state.State1, level, value);
            int newLevel2 = CompositionStep.Step(in _spec2, state.Level2, ref state.State2, level, value);

            if (newLevel1 == DdResult.False || newLevel2 == DdResult.False)
            {
                return DdResult.False;
            }

            if (newLevel1 == DdResult.True && newLevel2 == DdResult.True)
            {
                return DdResult.True;
            }

            state.Level1 = newLevel1;
            state.Level2 = newLevel2;
            return Math.Max(newLevel1, newLevel2);
        }

        /// <inheritdoc/>
        public bool StateEquals(in PairState<TState1, TState2> left, in PairState<TState1, TState2> right) =>
            left.Level1 == right.Level1
            && left.Level2 == right.Level2
            && _spec1.StateEquals(left.State1, right.State1)
            && _spec2.StateEquals(left.State2, right.State2);

        /// <inheritdoc/>
        public int StateHashCode(in PairState<TState1, TState2> state) =>
            HashCode.Combine(state.Level1, _spec1.StateHashCode(state.State1), state.Level2, _spec2.StateHashCode(state.State2));
    }
}

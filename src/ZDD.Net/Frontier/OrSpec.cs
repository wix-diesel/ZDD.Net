using System;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The union of two specs: the family of sets either <typeparamref name="TSpec1"/> or
    /// <typeparamref name="TSpec2"/> accepts, built directly — without ever materializing either
    /// spec's own family in full (docs/PLAN.md &#167;6.3). Built by <see cref="DdSpecExtensions"/>'s <c>Or</c>.
    /// </summary>
    /// <typeparam name="TSpec1">The first spec's type.</typeparam>
    /// <typeparam name="TState1">The first spec's state.</typeparam>
    /// <typeparam name="TSpec2">The second spec's type.</typeparam>
    /// <typeparam name="TState2">The second spec's state.</typeparam>
    /// <remarks>
    /// The mirror image of <see cref="AndSpec{TSpec1, TState1, TSpec2, TState2}"/>: both components are
    /// stepped at every composed level (see <see cref="CompositionStep"/>), but the terminal combination
    /// rule is De Morgan's dual. The composed branch resolves only once <i>both</i> components are done
    /// (neither has a real level left) — &#8868; if either of them is &#8868; (one side alone accepting is
    /// enough for a union), &#8869; only if both are &#8869;. While either component still has real levels
    /// left, the composition keeps going, driven by whichever remains real; a done component that is
    /// &#8868; keeps demanding "exclude the rest" for its own contribution, while a done &#8869; component
    /// contributes nothing and imposes no constraint.
    /// </remarks>
    public readonly struct OrSpec<TSpec1, TState1, TSpec2, TState2> : IDdSpec<PairState<TState1, TState2>>
        where TSpec1 : struct, IDdSpec<TState1>
        where TSpec2 : struct, IDdSpec<TState2>
    {
        private readonly TSpec1 _spec1;
        private readonly TSpec2 _spec2;

        /// <summary>Creates the union of <paramref name="spec1"/> and <paramref name="spec2"/>.</summary>
        /// <param name="spec1">The first spec.</param>
        /// <param name="spec2">The second spec.</param>
        public OrSpec(TSpec1 spec1, TSpec2 spec2)
        {
            _spec1 = spec1;
            _spec2 = spec2;
        }

        /// <inheritdoc/>
        public int GetRoot(ref PairState<TState1, TState2> state)
        {
            int level1 = _spec1.GetRoot(ref state.State1);
            int level2 = _spec2.GetRoot(ref state.State2);

            if (level1 is DdResult.True or DdResult.False)
            {
                state.State1 = default!;
            }

            if (level2 is DdResult.True or DdResult.False)
            {
                state.State2 = default!;
            }

            if (level1 < 1 && level2 < 1)
            {
                return level1 == DdResult.True || level2 == DdResult.True ? DdResult.True : DdResult.False;
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

            if (newLevel1 < 1 && newLevel2 < 1)
            {
                return newLevel1 == DdResult.True || newLevel2 == DdResult.True ? DdResult.True : DdResult.False;
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

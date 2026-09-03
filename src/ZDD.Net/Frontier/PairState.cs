namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The state <see cref="AndSpec{TSpec1, TState1, TSpec2, TState2}"/> and
    /// <see cref="OrSpec{TSpec1, TState1, TSpec2, TState2}"/> carry: each component's own state, plus the
    /// level it is next due at (see <see cref="CompositionStep"/> for what that level field encodes).
    /// </summary>
    /// <typeparam name="TState1">The first component's state.</typeparam>
    /// <typeparam name="TState2">The second component's state.</typeparam>
    public struct PairState<TState1, TState2>
    {
        /// <summary>The first component's own state, meaningful only while <see cref="Level1"/> is a real level.</summary>
        public TState1 State1;

        /// <summary>
        /// The first component's next due level (<c>1 .. VariableCount</c>), or one of two sentinels:
        /// <see cref="DdResult.True"/> ("only excluding the rest satisfies this component from here on")
        /// or <see cref="DdResult.False"/> ("this component already rejects, regardless of anything still
        /// to come"). See <see cref="CompositionStep"/> for how these are advanced.
        /// </summary>
        public int Level1;

        /// <summary>The second component's own state, meaningful only while <see cref="Level2"/> is a real level.</summary>
        public TState2 State2;

        /// <summary>The second component's next due level, or a sentinel — see <see cref="Level1"/>.</summary>
        public int Level2;
    }
}

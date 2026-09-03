namespace ZDD.Net.Frontier
{
    /// <summary>
    /// Advances one component of a composed spec (<see cref="AndSpec{TSpec1, TState1, TSpec2, TState2}"/>,
    /// <see cref="OrSpec{TSpec1, TState1, TSpec2, TState2}"/>) by one level of the composed traversal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A composed spec's two components rarely stay in lock-step: one may skip levels the other has a
    /// real decision at (<see cref="IDdSpec{TState}.GetChild"/>'s "skipped levels are excluded items"),
    /// or resolve to &#8868;/&#8869; while the other still has levels left. <see cref="PairState{TState1, TState2}"/>
    /// tracks each component's own next <i>due</i> level so the composed spec can tell, at each composed
    /// level, whether a component has something to say here or is still coasting on an earlier answer.
    /// </para>
    /// <para>
    /// The three states a component can be in all fit in one <c>int</c>, because they compose under the
    /// exact same rule for "not due yet":
    /// </para>
    /// <list type="bullet">
    /// <item><b>Real, due later</b> (level &#8805; 1, below the composed level): a genuine pending decision.
    /// Until reached, only excluding the item is valid — this is exactly what a skipped level means.</item>
    /// <item><b>&#8868; sentinel</b> (<see cref="DdResult.True"/>, <c>-1</c>): "only excluding everything
    /// remaining satisfies this component" — which, being level-independent, is never "due" at any real
    /// level and behaves identically to a level that is always still pending.</item>
    /// <item><b>&#8869; sentinel</b> (<see cref="DdResult.False"/>, <c>0</c>): this component has already
    /// rejected; nothing revives it, so it too is permanently "not due".</item>
    /// </list>
    /// <para>
    /// So the only real branch is "is this component's level exactly the composed level being decided
    /// right now?" — if not, including the item is invalid for it (it already committed to excluding
    /// everything up to whatever it is actually due at, or is dead, or is holding out for all-exclude),
    /// and excluding leaves it exactly as it was.
    /// </para>
    /// </remarks>
    internal static class CompositionStep
    {
        /// <summary>
        /// Advances one component's <paramref name="componentLevel"/> / <paramref name="componentState"/>
        /// past the composed level <paramref name="composedLevel"/>, along branch <paramref name="value"/>.
        /// </summary>
        /// <typeparam name="TSpec">The component spec's type.</typeparam>
        /// <typeparam name="TState">The component spec's state.</typeparam>
        /// <param name="spec">The component spec, only actually called when it is due.</param>
        /// <param name="componentLevel">The component's level field from <see cref="PairState{TState1, TState2}"/>.</param>
        /// <param name="componentState">The component's state field; updated in place.</param>
        /// <param name="composedLevel">The level the composed spec is deciding right now.</param>
        /// <param name="value">The branch taken: 0 excludes the item, 1 includes it.</param>
        /// <returns>
        /// The component's new level field: a real level below <paramref name="composedLevel"/>, or one
        /// of the two sentinels described on <see cref="PairState{TState1, TState2}.Level1"/>.
        /// </returns>
        public static int Step<TSpec, TState>(
            in TSpec spec, int componentLevel, ref TState componentState, int composedLevel, int value)
            where TSpec : struct, IDdSpec<TState>
        {
            if (componentLevel != composedLevel)
            {
                // Not due: a pending real level below composedLevel, a True sentinel (always "still
                // pending" on all-exclude), or a False sentinel (permanently not due). Including the
                // item is invalid in every one of those cases; excluding leaves the component untouched.
                if (value == 1)
                {
                    componentState = default!;
                    return DdResult.False;
                }

                return componentLevel;
            }

            int result = spec.GetChild(ref componentState, composedLevel, value);

            if (result == DdResult.True || result == DdResult.False)
            {
                // Canonicalize: once a component is done, its leftover state no longer distinguishes
                // anything, so every path that reaches the same sentinel must carry the same (default)
                // payload — otherwise PairState.StateEquals would needlessly keep them from merging.
                componentState = default!;
            }

            return result;
        }
    }
}

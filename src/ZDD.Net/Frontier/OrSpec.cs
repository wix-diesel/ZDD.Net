namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The state <see cref="OrSpec{TSpecA, TStateA, TSpecB, TStateB}"/> carries: each sub-spec's own
    /// state, plus the level at which that sub-spec is next due to make a real decision.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="AndState{TStateA, TStateB}"/>, a sub-spec's <c>Level</c> field here can also
    /// be <see cref="DdResult.False"/>: one sub-spec rejecting a branch does not reject the
    /// disjunction, so that sub-spec is retired ("dead" from here on — it can never turn a branch back
    /// to accepted) while the other keeps deciding.
    /// </remarks>
    /// <typeparam name="TStateA">The left sub-spec's state.</typeparam>
    /// <typeparam name="TStateB">The right sub-spec's state.</typeparam>
    public struct OrState<TStateA, TStateB>
    {
        internal TStateA StateA;
        internal int LevelA;
        internal TStateB StateB;
        internal int LevelB;
    }

    /// <summary>
    /// The disjunction of two specs: the family of sets accepted by <c>specA</c>, <c>specB</c>, or
    /// both, built directly — without ever materializing either sub-spec's own ZDD.
    /// </summary>
    /// <typeparam name="TSpecA">The left sub-spec's type.</typeparam>
    /// <typeparam name="TStateA">The left sub-spec's state.</typeparam>
    /// <typeparam name="TSpecB">The right sub-spec's type.</typeparam>
    /// <typeparam name="TStateB">The right sub-spec's state.</typeparam>
    /// <remarks>
    /// <para>
    /// <b>Level synchronization</b> follows the same rule as <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/>:
    /// the composed level is the higher of the two sub-specs' next real decision levels, and a
    /// sub-spec that is mid-skip implicitly requires exclusion until it gets there. The difference is
    /// what happens when that implicit requirement is broken (<c>value == 1</c> chosen for a
    /// sub-spec that is not due): for <c>And</c> the whole conjunction dies, but for <c>Or</c> only
    /// that one sub-spec's contribution dies (it goes "dead", see <see cref="OrState{TStateA, TStateB}"/>)
    /// — the other sub-spec, unaffected, keeps the branch alive on its own.
    /// </para>
    /// <para>
    /// A sub-spec that has already accepted (<see cref="DdResult.True"/>) behaves like one still
    /// pending at a level below every real level: choosing to include a further item breaks its
    /// implicit "everything else excluded" completion, degrading it to dead exactly like a broken
    /// skip does — it does not end the whole disjunction, since accepting is a per-branch fact, not a
    /// standing guarantee for every continuation of that branch.
    /// </para>
    /// <para>
    /// Composes as a plain <c>struct</c> of two <c>struct</c> sub-states — no boxing, no virtual
    /// calls — so <c>a.Or(b).Or(c)</c> nests to any depth (docs/frontier-guide.md §6.2).
    /// </para>
    /// </remarks>
    public readonly struct OrSpec<TSpecA, TStateA, TSpecB, TStateB> : IDdSpec<OrState<TStateA, TStateB>>
        where TSpecA : struct, IDdSpec<TStateA>
        where TSpecB : struct, IDdSpec<TStateB>
    {
        private readonly TSpecA _specA;
        private readonly TSpecB _specB;

        /// <summary>Creates the disjunction of <paramref name="specA"/> and <paramref name="specB"/>.</summary>
        public OrSpec(TSpecA specA, TSpecB specB)
        {
            _specA = specA;
            _specB = specB;
        }

        /// <inheritdoc/>
        public int GetRoot(ref OrState<TStateA, TStateB> state)
        {
            int levelA = _specA.GetRoot(ref state.StateA);
            int levelB = _specB.GetRoot(ref state.StateB);
            return Combine(ref state, levelA, levelB);
        }

        /// <inheritdoc/>
        public int GetChild(ref OrState<TStateA, TStateB> state, int level, int value)
        {
            int levelA = StepA(ref state, level, value);
            int levelB = StepB(ref state, level, value);
            return Combine(ref state, levelA, levelB);
        }

        /// <summary>Advances the left sub-spec, or applies the implicit-exclusion rule if it is not due yet.</summary>
        private int StepA(ref OrState<TStateA, TStateB> state, int level, int value)
        {
            if (state.LevelA == level)
            {
                return _specA.GetChild(ref state.StateA, level, value);
            }

            return value == 0 ? state.LevelA : DdResult.False;
        }

        /// <summary>Advances the right sub-spec, or applies the implicit-exclusion rule if it is not due yet.</summary>
        private int StepB(ref OrState<TStateA, TStateB> state, int level, int value)
        {
            if (state.LevelB == level)
            {
                return _specB.GetChild(ref state.StateB, level, value);
            }

            return value == 0 ? state.LevelB : DdResult.False;
        }

        /// <summary>
        /// Folds the two sub-results into the composed level, canonicalizing a sub-state that just
        /// finished (accepted or died) to <see langword="default"/> so states that differ only in a
        /// no-longer-relevant sub-state still merge.
        /// </summary>
        private static int Combine(ref OrState<TStateA, TStateB> state, int levelA, int levelB)
        {
            if (levelA <= 0)
            {
                state.StateA = default!;
            }

            if (levelB <= 0)
            {
                state.StateB = default!;
            }

            state.LevelA = levelA;
            state.LevelB = levelB;

            if (levelA <= 0 && levelB <= 0)
            {
                // Both settled (accepted or dead): the disjunction accepts iff either side did.
                return levelA == DdResult.True || levelB == DdResult.True ? DdResult.True : DdResult.False;
            }

            return levelA > levelB ? levelA : levelB;
        }

        /// <inheritdoc/>
        public bool StateEquals(in OrState<TStateA, TStateB> left, in OrState<TStateA, TStateB> right)
        {
            if (left.LevelA != right.LevelA || left.LevelB != right.LevelB)
            {
                return false;
            }

            bool equalA = left.LevelA <= 0 || _specA.StateEquals(left.StateA, right.StateA);
            bool equalB = left.LevelB <= 0 || _specB.StateEquals(left.StateB, right.StateB);
            return equalA && equalB;
        }

        /// <inheritdoc/>
        public int StateHashCode(in OrState<TStateA, TStateB> state)
        {
            int hashA = state.LevelA <= 0 ? 0 : _specA.StateHashCode(state.StateA);
            int hashB = state.LevelB <= 0 ? 0 : _specB.StateHashCode(state.StateB);
            return System.HashCode.Combine(state.LevelA, hashA, state.LevelB, hashB);
        }
    }
}

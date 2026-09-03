namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The state <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/> carries: each sub-spec's own
    /// state, plus the level at which that sub-spec is next due to make a real decision.
    /// </summary>
    /// <remarks>
    /// A sub-spec's <c>Level</c> field mirrors the value its own <c>GetRoot</c>/<c>GetChild</c> last
    /// returned: a positive level while it still has decisions ahead, or <see cref="DdResult.True"/>
    /// once it has accepted (meaning every further item must be excluded for that sub-spec to hold).
    /// It can never be <see cref="DdResult.False"/> here — as soon as either sub-spec is unsatisfiable,
    /// the whole conjunction is, and <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/> reports
    /// that immediately rather than keeping the (now-meaningless) state around.
    /// </remarks>
    /// <typeparam name="TStateA">The left sub-spec's state.</typeparam>
    /// <typeparam name="TStateB">The right sub-spec's state.</typeparam>
    public struct AndState<TStateA, TStateB>
    {
        internal TStateA StateA;
        internal int LevelA;
        internal TStateB StateB;
        internal int LevelB;
    }

    /// <summary>
    /// The conjunction of two specs: the family of sets accepted by both <c>specA</c> and <c>specB</c>
    /// at once, built directly — without ever materializing either sub-spec's own ZDD.
    /// </summary>
    /// <typeparam name="TSpecA">The left sub-spec's type.</typeparam>
    /// <typeparam name="TStateA">The left sub-spec's state.</typeparam>
    /// <typeparam name="TSpecB">The right sub-spec's type.</typeparam>
    /// <typeparam name="TStateB">The right sub-spec's state.</typeparam>
    /// <remarks>
    /// <para>
    /// <b>Level synchronization.</b> The two sub-specs are not required to decide at the same levels:
    /// either may skip ahead (docs/frontier-spec-guide.md §3). At any point, this spec's own next
    /// level is the <em>higher</em> (nearer-root) of the two sub-specs' next real decision levels —
    /// the other sub-spec is still mid-skip, silently requiring every item until its own next level to
    /// be excluded. So when the composed spec is asked to decide a level only one sub-spec actually
    /// owns, including the item (<c>value == 1</c>) would contradict the skipping sub-spec's implicit
    /// exclusion, and the whole conjunction dies right there; excluding it (<c>value == 0</c>) leaves
    /// that sub-spec's state untouched, still waiting for its own level.
    /// </para>
    /// <para>
    /// Composes as a plain <c>struct</c> of two <c>struct</c> sub-states — no boxing, no virtual
    /// calls — so <c>a.And(b).And(c)</c> nests to any depth and each level still devirtualizes and
    /// inlines exactly as a hand-written spec would (docs/frontier-guide.md §6.2).
    /// </para>
    /// </remarks>
    public readonly struct AndSpec<TSpecA, TStateA, TSpecB, TStateB> : IDdSpec<AndState<TStateA, TStateB>>
        where TSpecA : struct, IDdSpec<TStateA>
        where TSpecB : struct, IDdSpec<TStateB>
    {
        private readonly TSpecA _specA;
        private readonly TSpecB _specB;

        /// <summary>Creates the conjunction of <paramref name="specA"/> and <paramref name="specB"/>.</summary>
        public AndSpec(TSpecA specA, TSpecB specB)
        {
            _specA = specA;
            _specB = specB;
        }

        /// <inheritdoc/>
        public int GetRoot(ref AndState<TStateA, TStateB> state)
        {
            int levelA = _specA.GetRoot(ref state.StateA);
            if (levelA == DdResult.False)
            {
                return DdResult.False;
            }

            int levelB = _specB.GetRoot(ref state.StateB);
            if (levelB == DdResult.False)
            {
                return DdResult.False;
            }

            return Combine(ref state, levelA, levelB);
        }

        /// <inheritdoc/>
        public int GetChild(ref AndState<TStateA, TStateB> state, int level, int value)
        {
            int levelA = StepA(ref state, level, value);
            if (levelA == DdResult.False)
            {
                return DdResult.False;
            }

            int levelB = StepB(ref state, level, value);
            if (levelB == DdResult.False)
            {
                return DdResult.False;
            }

            return Combine(ref state, levelA, levelB);
        }

        /// <summary>Advances the left sub-spec, or applies the implicit-exclusion rule if it is not due yet.</summary>
        private int StepA(ref AndState<TStateA, TStateB> state, int level, int value)
        {
            if (state.LevelA == level)
            {
                return _specA.GetChild(ref state.StateA, level, value);
            }

            return value == 0 ? state.LevelA : DdResult.False;
        }

        /// <summary>Advances the right sub-spec, or applies the implicit-exclusion rule if it is not due yet.</summary>
        private int StepB(ref AndState<TStateA, TStateB> state, int level, int value)
        {
            if (state.LevelB == level)
            {
                return _specB.GetChild(ref state.StateB, level, value);
            }

            return value == 0 ? state.LevelB : DdResult.False;
        }

        /// <summary>
        /// Folds the two (already false-checked) sub-results into the composed level, canonicalizing
        /// a sub-state that just finished to <see langword="default"/> so states that differ only in a
        /// no-longer-relevant sub-state still merge.
        /// </summary>
        private static int Combine(ref AndState<TStateA, TStateB> state, int levelA, int levelB)
        {
            if (levelA == DdResult.True)
            {
                state.StateA = default!;
            }

            if (levelB == DdResult.True)
            {
                state.StateB = default!;
            }

            state.LevelA = levelA;
            state.LevelB = levelB;

            if (levelA == DdResult.True && levelB == DdResult.True)
            {
                return DdResult.True;
            }

            return levelA > levelB ? levelA : levelB;
        }

        /// <inheritdoc/>
        public bool StateEquals(in AndState<TStateA, TStateB> left, in AndState<TStateA, TStateB> right)
        {
            if (left.LevelA != right.LevelA || left.LevelB != right.LevelB)
            {
                return false;
            }

            bool equalA = left.LevelA == DdResult.True || _specA.StateEquals(left.StateA, right.StateA);
            bool equalB = left.LevelB == DdResult.True || _specB.StateEquals(left.StateB, right.StateB);
            return equalA && equalB;
        }

        /// <inheritdoc/>
        public int StateHashCode(in AndState<TStateA, TStateB> state)
        {
            int hashA = state.LevelA == DdResult.True ? 0 : _specA.StateHashCode(state.StateA);
            int hashB = state.LevelB == DdResult.True ? 0 : _specB.StateHashCode(state.StateB);
            return System.HashCode.Combine(state.LevelA, hashA, state.LevelB, hashB);
        }
    }
}

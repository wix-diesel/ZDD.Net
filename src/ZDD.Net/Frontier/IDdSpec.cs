namespace ZDD.Net.Frontier
{
    /// <summary>
    /// A frontier-search specification: the state machine a builder unrolls, level by level, into a ZDD.
    /// Levels run from <c>VariableCount</c> (root side) down to <c>1</c>, with <c>item = VariableCount - level</c>.
    /// </summary>
    /// <typeparam name="TState">
    /// The state carried between levels. Use a <c>struct</c>: it is stored inline and each branch gets its
    /// own copy. A reference type works, but only the reference is copied, so it must not be mutated in place.
    /// </typeparam>
    /// <example>
    /// A spec accepting exactly the sets with no three consecutively selected items, whose state
    /// is the current run length (docs/frontier-spec-guide.md walks through this build-up):
    /// <code>
    /// public readonly struct NoThreeConsecutiveSpec : IDdSpec&lt;int&gt;
    /// {
    ///     private readonly int _itemCount;
    ///
    ///     public NoThreeConsecutiveSpec(int itemCount) =&gt; _itemCount = itemCount;
    ///
    ///     public int GetRoot(ref int run)
    ///     {
    ///         run = 0;
    ///         return _itemCount;
    ///     }
    ///
    ///     public int GetChild(ref int run, int level, int value)
    ///     {
    ///         if (value == 0)
    ///         {
    ///             run = 0;
    ///         }
    ///         else
    ///         {
    ///             run++;
    ///             if (run &gt;= 3)
    ///             {
    ///                 return DdResult.False; // pruned: three in a row is already invalid
    ///             }
    ///         }
    ///
    ///         int remaining = level - 1;
    ///         return remaining == 0 ? DdResult.True : remaining;
    ///     }
    ///
    ///     public bool StateEquals(in int left, in int right) =&gt; left == right;
    ///     public int StateHashCode(in int state) =&gt; state;
    /// }
    ///
    /// using ZddManager manager = new ZddManager(variableCount: 8);
    /// Zdd family = FrontierBuilder.Build&lt;NoThreeConsecutiveSpec, int&gt;(manager, new NoThreeConsecutiveSpec(8));
    /// </code>
    /// </example>
    /// <remarks>
    /// A state must hold only what still affects later transitions, in canonical form; anything else splits
    /// the level's state set and the width explodes. Full contract: <c>docs/frontier-spec-guide.md</c>.
    /// A wide-enough level is expanded by calling <see cref="GetChild"/> from several threads at once
    /// (<see cref="BuildOptions.MaxDegreeOfParallelism"/>, M4-3): the spec value itself is copied
    /// per-thread automatically as long as it is a <c>readonly struct</c>, but it must not hold a
    /// reference to anything it mutates — see docs/frontier-spec-guide.md §4.
    /// </remarks>
    public interface IDdSpec<TState>
    {
        /// <summary>Initializes the root state and returns its level.</summary>
        /// <param name="state">Receives the root state; default-initialized on entry.</param>
        /// <returns>The root's level, or <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.</returns>
        int GetRoot(ref TState state);

        /// <summary>Moves <paramref name="state"/> along the <paramref name="value"/> branch and returns the child's level.</summary>
        /// <param name="state">The state at <paramref name="level"/> on entry, the child's on return; a per-branch copy, so overwrite it in place.</param>
        /// <param name="level">The level being decided; the item is <c>VariableCount - level</c>.</param>
        /// <param name="value">The branch taken: <c>0</c> excludes the item, <c>1</c> includes it.</param>
        /// <returns>
        /// The child's level, which must be in <c>1 .. level - 1</c> (skipped levels are excluded items), or
        /// <see cref="DdResult.False"/> / <see cref="DdResult.True"/> (&#8868; excludes every remaining item).
        /// </returns>
        int GetChild(ref TState state, int level, int value);

        /// <summary>
        /// Tests whether two states at the same level are interchangeable from here on.
        /// This is what merges branches, so it should ignore whatever no longer matters.
        /// </summary>
        /// <param name="left">The left state.</param>
        /// <param name="right">The right state.</param>
        bool StateEquals(in TState left, in TState right);

        /// <summary>
        /// Returns a hash code for a state. States that <see cref="StateEquals"/> accepts must hash equally,
        /// or the level's state table keeps duplicates and builds the same sub-diagram twice.
        /// </summary>
        /// <param name="state">The state to hash.</param>
        int StateHashCode(in TState state);
    }
}

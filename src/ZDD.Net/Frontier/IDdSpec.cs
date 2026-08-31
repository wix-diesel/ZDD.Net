namespace ZDD.Net.Frontier
{
    /// <summary>
    /// A frontier-search specification: the state machine a builder unrolls, level by level, into a ZDD.
    /// Write the transition and the ZDD is constructed for you, without ever materializing the sets.
    /// </summary>
    /// <typeparam name="TState">
    /// The state carried between levels. A <c>struct</c> is strongly recommended: the builder stores
    /// states inline and calls through a <c>struct</c> type parameter, so no allocation or dispatch remains.
    /// </typeparam>
    /// <remarks>
    /// Levels run from <c>VariableCount</c> (root side, item 0) down to <c>1</c> (bottom, the last item);
    /// <c>item = VariableCount - level</c>, the same convention the core engine uses internally.
    /// Every method must be a pure function of its arguments — the builder calls them in an unspecified
    /// order and reuses results across branches, so a specification must not depend on call order or
    /// on mutable state of its own.
    /// The decisive design rule is that a state must hold <b>only what still affects future transitions</b>,
    /// in a canonical form: two histories that behave identically from here on must produce equal states,
    /// or the level's state set splits and the diagram's width explodes.
    /// See <c>docs/frontier-spec-guide.md</c> for the full contract and worked examples.
    /// </remarks>
    /// <example>
    /// Sets of exactly <c>k</c> items out of <c>n</c>; the state is how many have been taken so far:
    /// <code>
    /// public readonly struct ExactlyKSpec : IDdSpec&lt;int&gt;
    /// {
    ///     private readonly int _itemCount;
    ///     private readonly int _k;
    ///
    ///     public ExactlyKSpec(int itemCount, int k) { _itemCount = itemCount; _k = k; }
    ///
    ///     public int GetRoot(ref int taken)
    ///     {
    ///         taken = 0;
    ///         return _itemCount;                                  // level n decides item 0
    ///     }
    ///
    ///     public int GetChild(ref int taken, int level, int value)
    ///     {
    ///         taken += value;
    ///         if (taken &gt; _k) { return DdResult.False; }
    ///         if (taken == _k) { return DdResult.True; }           // the remaining items stay excluded
    ///
    ///         int remaining = level - 1;
    ///         if (taken + remaining &lt; _k) { return DdResult.False; }
    ///
    ///         return remaining;
    ///     }
    ///
    ///     public bool StateEquals(in int left, in int right) =&gt; left == right;
    ///     public int StateHashCode(in int state) =&gt; state;
    /// }
    /// </code>
    /// </example>
    public interface IDdSpec<TState>
    {
        /// <summary>Initializes the root state and returns its level.</summary>
        /// <param name="state">
        /// Receives the root state. Starts out default-initialized and is owned by the builder;
        /// the implementation must overwrite every field it later reads.
        /// </param>
        /// <returns>
        /// The level of the root, in <c>1 .. VariableCount</c>, or <see cref="DdResult.False"/> /
        /// <see cref="DdResult.True"/> for a specification that is decided before any item is examined.
        /// </returns>
        int GetRoot(ref TState state);

        /// <summary>Moves <paramref name="state"/> along the <paramref name="value"/> branch and returns the child's level.</summary>
        /// <param name="state">
        /// On entry the state at <paramref name="level"/>, on return the child's state. The builder passes
        /// a private copy, so overwriting it in place is expected and never disturbs the sibling branch.
        /// The contents are ignored when a terminal is returned.
        /// </param>
        /// <param name="level">The level being decided, in <c>1 .. VariableCount</c>; the item is <c>VariableCount - level</c>.</param>
        /// <param name="value">The branch taken: <c>0</c> excludes the item, <c>1</c> includes it.</param>
        /// <returns>
        /// The child's level, which must be <b>strictly less than <paramref name="level"/></b> and at least <c>1</c>
        /// (skipped levels are excluded items), or <see cref="DdResult.False"/> / <see cref="DdResult.True"/>.
        /// Returning a level that is not below <paramref name="level"/> would make construction loop.
        /// </returns>
        int GetChild(ref TState state, int level, int value);

        /// <summary>Tests whether two states at the same level are interchangeable from here on.</summary>
        /// <param name="left">The left state.</param>
        /// <param name="right">The right state.</param>
        /// <remarks>
        /// This is what merges branches, so it should ignore anything that no longer matters (stale slots,
        /// padding, bookkeeping). It must be an equivalence relation and agree with <see cref="StateHashCode"/>.
        /// </remarks>
        bool StateEquals(in TState left, in TState right);

        /// <summary>Returns a hash code for a state.</summary>
        /// <param name="state">The state to hash.</param>
        /// <remarks>
        /// States that <see cref="StateEquals"/> accepts must hash equally, or the level's state table
        /// keeps duplicates and the same sub-diagram is built more than once.
        /// </remarks>
        int StateHashCode(in TState state);
    }
}

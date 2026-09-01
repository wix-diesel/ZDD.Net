namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The terminal values returned by <see cref="IDdSpec{TState}"/> and its siblings.
    /// Any other return value is the level of the child state, which must be positive.
    /// </summary>
    /// <remarks>
    /// The encoding is TdZdd's, kept deliberately so that a specification written against
    /// TdZdd's <c>DdSpec</c> ports unchanged.
    /// </remarks>
    public static class DdResult
    {
        /// <summary>The &#8869; terminal (&#8709;): this branch leads to no set at all and is pruned.</summary>
        public const int False = 0;

        /// <summary>
        /// The &#8868; terminal (<c>{&#8709;}</c>): the choices made so far form an accepted set.
        /// Zero-suppression means every remaining item is excluded, not free.
        /// </summary>
        public const int True = -1;

        /// <summary>Tests whether a returned value is one of the two terminals rather than a level.</summary>
        /// <param name="result">A value returned by <c>GetRoot</c> or <c>GetChild</c>.</param>
        public static bool IsTerminal(int result) => result is False or True;
    }
}

namespace ZDD.Net.Core
{
    /// <summary>
    /// The order in which <see cref="Zdd.Sets(ZddEnumerationOrder)"/> yields sets.
    /// </summary>
    /// <remarks>
    /// Both orders are produced by a single depth-first traversal, so choosing between them
    /// costs nothing extra; they differ only in whether a node's 0-branch or 1-branch is
    /// visited first. Note the two "lexicographic" orders disagree: e.g. for <c>{0,2}</c> vs
    /// <c>{1}</c>, <see cref="Default"/> yields <c>{1}</c> before <c>{0,2}</c>, while
    /// <see cref="Lexicographic"/> yields the opposite.
    /// </remarks>
    public enum ZddEnumerationOrder
    {
        /// <summary>Default. Depth-first, 0-branch first — lexicographic order of the indicator vector.</summary>
        /// <remarks>
        /// Treats each set as its 0/1 indicator sequence over items 0, 1, 2, …. Since item 0 sits
        /// at the root, this is a plain 0-branch-first traversal with no sorting or lookahead needed.
        /// </remarks>
        Default = 0,

        /// <summary>
        /// Lexicographic order of the set's items in ascending order:
        /// <c>{}</c> &lt; <c>{0}</c> &lt; <c>{0,1}</c> &lt; <c>{0,2}</c> &lt; <c>{1}</c> &lt; <c>{2}</c>.
        /// </summary>
        /// <remarks>
        /// The empty set sorts first (as the prefix of every sequence), then by ascending first
        /// element. Implemented as a 1-branch-first traversal that peeks the 0-branch's terminal
        /// first so the "remainder is empty" case is emitted before recursing into item elements.
        /// </remarks>
        Lexicographic = 1,
    }
}

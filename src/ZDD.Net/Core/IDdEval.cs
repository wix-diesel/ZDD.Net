namespace ZDD.Net.Core
{
    /// <summary>
    /// A "folding" evaluator that walks a ZDD bottom-up and reduces it to a single value.
    /// Cardinality, probability, and weight optimization are all expressible this way.
    /// </summary>
    /// <typeparam name="TValue">The type of intermediate and final results.</typeparam>
    /// <remarks>
    /// Implement only <see cref="EvalTerminal"/> (value at each terminal) and <see cref="EvalNode"/>
    /// (how to combine child values); <see cref="ZddEvaluation.Evaluate{TEval, TValue}"/> handles
    /// traversal order, memoization, and the explicit stack.
    /// Implementations must be a <c>struct</c> — the API takes <c>where TEval : struct, IDdEval&lt;TValue&gt;</c>
    /// so the JIT can specialize and inline <see cref="EvalNode"/> instead of dispatching through an interface.
    /// <see cref="EvalNode"/> is called exactly once per reachable non-terminal node (memoized, not once per set),
    /// after both children's values are known; skipped items never appear because zero-suppression means
    /// no node has a 1-branch to the false terminal.
    /// </remarks>
    /// <example>
    /// Counting the sets in a family (equivalent to <see cref="CardinalityEval"/>):
    /// <code>
    /// public readonly struct MyCountEval : IDdEval&lt;BigInteger&gt;
    /// {
    ///     public BigInteger EvalTerminal(bool isTrue) =&gt; isTrue ? BigInteger.One : BigInteger.Zero;
    ///     public BigInteger EvalNode(int item, BigInteger lo, BigInteger hi) =&gt; lo + hi;
    /// }
    ///
    /// BigInteger count = family.Evaluate&lt;MyCountEval, BigInteger&gt;(default);
    /// </code>
    /// </example>
    public interface IDdEval<TValue>
    {
        /// <summary>Returns the value of a terminal.</summary>
        /// <param name="isTrue"><see langword="true"/> for terminal &#8868; (<c>{&#8709;}</c>); <see langword="false"/> for terminal &#8869; (&#8709;).</param>
        /// <remarks>Called once for each of <see langword="false"/> and <see langword="true"/>, before traversal starts.</remarks>
        TValue EvalTerminal(bool isTrue);

        /// <summary>Combines the child values for one non-terminal node.</summary>
        /// <param name="item">The item index this node branches on.</param>
        /// <param name="lo">The evaluated value of the branch that excludes <paramref name="item"/>.</param>
        /// <param name="hi">The evaluated value of the branch that includes <paramref name="item"/> (already stripped of it).</param>
        TValue EvalNode(int item, TValue lo, TValue hi);
    }
}

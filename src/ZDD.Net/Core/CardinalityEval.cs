using System.Numerics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// Evaluator that exactly counts the number of sets in the family (its cardinality).
    /// Backs <see cref="Zdd.Count"/>.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="BigInteger"/> because cardinality grows exponentially with the
    /// number of variables (2^n for n variables), overflowing 64 bits past 64 variables.
    /// For speed over exactness, use <see cref="ApproximateCardinalityEval"/> instead.
    /// </remarks>
    public readonly struct CardinalityEval : IDdEval<BigInteger>
    {
        /// <inheritdoc/>
        public BigInteger EvalTerminal(bool isTrue) => isTrue ? BigInteger.One : BigInteger.Zero;

        /// <inheritdoc/>
        public BigInteger EvalNode(int item, BigInteger lo, BigInteger hi) => lo + hi;
    }
}

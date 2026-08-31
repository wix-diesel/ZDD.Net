namespace ZDD.Net.Core
{
    /// <summary>
    /// Evaluator that approximates the number of sets in the family as a <see cref="double"/>.
    /// Backs <see cref="Zdd.CountApprox"/>.
    /// </summary>
    /// <remarks>
    /// Same recurrence as <see cref="CardinalityEval"/> but faster, since <see cref="double"/>
    /// addition is O(1) versus <see cref="System.Numerics.BigInteger"/>. Exact while the
    /// cardinality fits in 53 bits (2^53); beyond that low-order digits round off, and past
    /// <see cref="double.MaxValue"/> the result saturates to <see cref="double.PositiveInfinity"/>
    /// (never throws, never negative or NaN). Use <see cref="Zdd.Count"/> when exactness matters.
    /// </remarks>
    public readonly struct ApproximateCardinalityEval : IDdEval<double>
    {
        /// <inheritdoc/>
        public double EvalTerminal(bool isTrue) => isTrue ? 1.0 : 0.0;

        /// <inheritdoc/>
        public double EvalNode(int item, double lo, double hi) => lo + hi;
    }
}

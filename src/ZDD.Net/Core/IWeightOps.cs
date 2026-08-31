namespace ZDD.Net.Core
{
    /// <summary>
    /// Strategy supplying "zero", "add", and "compare" for a weight type <typeparamref name="TWeight"/>.
    /// Used by weight optimization (<see cref="Zdd.MaxWeight{TWeight, TOps}"/>,
    /// <see cref="Zdd.MinWeight{TWeight, TOps}"/>, <see cref="Zdd.TopK{TWeight, TOps}"/>).
    /// </summary>
    /// <typeparam name="TWeight">The weight type.</typeparam>
    /// <remarks>
    /// Passed as a type parameter (<c>static abstract</c>, implementation must be a <c>struct</c>) so the
    /// JIT generates specialized code per weight type instead of dispatching through an interface.
    /// <see cref="Compare"/> must be a total order; <see cref="Add"/> must be associative with
    /// <see cref="Zero"/> as identity (commutativity is not required).
    /// </remarks>
    /// <example>
    /// A rational-number weight:
    /// <code>
    /// public readonly record struct Rational(long Numerator, long Denominator);
    ///
    /// public readonly struct RationalWeightOps : IWeightOps&lt;Rational&gt;
    /// {
    ///     public static Rational Zero =&gt; new Rational(0, 1);
    ///
    ///     public static Rational Add(Rational left, Rational right) =&gt;
    ///         Reduce(left.Numerator * right.Denominator + right.Numerator * left.Denominator,
    ///                left.Denominator * right.Denominator);
    ///
    ///     public static int Compare(Rational left, Rational right) =&gt;
    ///         (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator);
    /// }
    ///
    /// WeightedSet&lt;Rational&gt; best = family.MaxWeight&lt;Rational, RationalWeightOps&gt;(weights);
    /// </code>
    /// </example>
    public interface IWeightOps<TWeight>
    {
        /// <summary>The additive identity; also the weight of the empty set.</summary>
        static abstract TWeight Zero { get; }

        /// <summary>Adds two weights.</summary>
        /// <param name="left">The left weight.</param>
        /// <param name="right">The right weight.</param>
        static abstract TWeight Add(TWeight left, TWeight right);

        /// <summary>Compares two weights.</summary>
        /// <param name="left">The left weight.</param>
        /// <param name="right">The right weight.</param>
        /// <returns>Negative if <paramref name="left"/> is smaller, zero if equal, positive if larger.</returns>
        static abstract int Compare(TWeight left, TWeight right);
    }
}

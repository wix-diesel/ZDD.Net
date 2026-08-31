using System.Numerics;

namespace ZDD.Net.Core
{
    /// <summary>Default weight strategy for <see cref="int"/>.</summary>
    /// <remarks>
    /// Addition is checked: an overflowing sum throws <see cref="System.OverflowException"/>
    /// rather than silently wrapping into a wrong answer. Use <see cref="Int64WeightOps"/> or
    /// <see cref="BigIntegerWeightOps"/> for larger magnitudes.
    /// </remarks>
    public readonly struct Int32WeightOps : IWeightOps<int>
    {
        /// <inheritdoc/>
        public static int Zero => 0;

        /// <inheritdoc/>
        public static int Add(int left, int right) => checked(left + right);

        /// <inheritdoc/>
        public static int Compare(int left, int right) => left.CompareTo(right);
    }

    /// <summary>Default weight strategy for <see cref="long"/>.</summary>
    /// <remarks>Addition is checked, for the same reason as <see cref="Int32WeightOps"/>.</remarks>
    public readonly struct Int64WeightOps : IWeightOps<long>
    {
        /// <inheritdoc/>
        public static long Zero => 0L;

        /// <inheritdoc/>
        public static long Add(long left, long right) => checked(left + right);

        /// <inheritdoc/>
        public static int Compare(long left, long right) => left.CompareTo(right);
    }

    /// <summary>Default weight strategy for <see cref="double"/>.</summary>
    /// <remarks>
    /// Floating-point addition is not associative, so results can differ slightly depending on
    /// summation order; near-tied weights may break differently as a result. <see cref="double.NaN"/>
    /// sorts as smaller than any other value and does not throw, but is otherwise meaningless here.
    /// Use <see cref="Int64WeightOps"/> or <see cref="BigIntegerWeightOps"/> when exact comparison matters.
    /// </remarks>
    public readonly struct DoubleWeightOps : IWeightOps<double>
    {
        /// <inheritdoc/>
        public static double Zero => 0.0;

        /// <inheritdoc/>
        public static double Add(double left, double right) => left + right;

        /// <inheritdoc/>
        public static int Compare(double left, double right) => left.CompareTo(right);
    }

    /// <summary>Default weight strategy for <see cref="BigInteger"/>. No overflow, no rounding.</summary>
    /// <remarks>Addition cost grows with digit count; prefer <see cref="Int64WeightOps"/> when values fit.</remarks>
    public readonly struct BigIntegerWeightOps : IWeightOps<BigInteger>
    {
        /// <inheritdoc/>
        public static BigInteger Zero => BigInteger.Zero;

        /// <inheritdoc/>
        public static BigInteger Add(BigInteger left, BigInteger right) => left + right;

        /// <inheritdoc/>
        public static int Compare(BigInteger left, BigInteger right) => left.CompareTo(right);
    }
}

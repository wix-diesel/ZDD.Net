using System.Numerics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// <see cref="int"/> を重みにする既定の戦略。
    /// </summary>
    /// <remarks>
    /// <b>足し算は checked</b>: 集合の重みは要素の重みの総和なので、変数が多いと <see cref="int"/> は
    /// 案外あっさり溢れる。黙って折り返すと「最大重みのはずが負の値」という<b>静かに誤った答</b>に
    /// なるので、溢れたら <see cref="System.OverflowException"/> にする。
    /// 溢れうる大きさを扱うなら <see cref="Int64WeightOps"/> か <see cref="BigIntegerWeightOps"/> を使う。
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

    /// <summary>
    /// <see cref="long"/> を重みにする既定の戦略。
    /// </summary>
    /// <remarks>
    /// 足し算は <see cref="Int32WeightOps"/> と同じ理由で checked。
    /// </remarks>
    public readonly struct Int64WeightOps : IWeightOps<long>
    {
        /// <inheritdoc/>
        public static long Zero => 0L;

        /// <inheritdoc/>
        public static long Add(long left, long right) => checked(left + right);

        /// <inheritdoc/>
        public static int Compare(long left, long right) => left.CompareTo(right);
    }

    /// <summary>
    /// <see cref="double"/> を重みにする既定の戦略。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>足す順序で結果が変わりうる</b>: 浮動小数の加算は結合的ではないので、
    /// 桁の離れた重みが混ざると「同じ集合なのに求め方によって最後の 1bit が違う」ことが起きる。
    /// 重みが同点に近いときにどちらが選ばれるかは、この誤差に左右される。
    /// 厳密な比較が要るなら <see cref="Int64WeightOps"/> か
    /// <see cref="BigIntegerWeightOps"/>（あるいは有理数の自前実装）を使う。
    /// </para>
    /// <para>
    /// <b><see cref="double.NaN"/></b>: 比較は <see cref="double.CompareTo(double)"/> に従い、
    /// <see cref="double.NaN"/> はどの値よりも小さいものとして順序づけられる（全順序にはなる）。
    /// 重みに <see cref="double.NaN"/> を混ぜても例外にはならないが、結果に意味は無い。
    /// </para>
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

    /// <summary>
    /// <see cref="BigInteger"/> を重みにする既定の戦略。桁溢れも丸めも起きない。
    /// </summary>
    /// <remarks>
    /// 加算は桁数に比例した時間とアロケーションを伴うので、収まるなら
    /// <see cref="Int64WeightOps"/> のほうが桁違いに軽い（docs/OPEN-QUESTIONS.md B10）。
    /// </remarks>
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

namespace ZDD.Net.Core
{
    /// <summary>
    /// 族に属する集合の個数を <see cref="double"/> で近似して数える評価器。
    /// <see cref="Zdd.CountApprox"/> の中身。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 漸化式は <see cref="CardinalityEval"/> と同じ <c>lo + hi</c> で、値の型だけが違う。
    /// <see cref="System.Numerics.BigInteger"/> の加算は桁数に比例した時間とアロケーションを伴うのに対し、
    /// <see cref="double"/> の加算は 1 命令で済む（docs/PLAN.md §10-5）。
    /// 「およその規模が分かればよい」場面はこちらで足りる。
    /// </para>
    /// <para>
    /// <b>誤差</b>: <see cref="double"/> の仮数部は 53bit なので、濃度が 2^53 を超えると
    /// 下位の桁が丸められる。それ以下なら結果は<b>厳密</b>（整数どうしの加算しか行わないため）。
    /// </para>
    /// <para>
    /// <b>桁溢れ</b>: 濃度が <see cref="double.MaxValue"/>（およそ 1.8 × 10^308）を超えると、
    /// IEEE 754 の規定どおり <see cref="double.PositiveInfinity"/> になる。
    /// 例外にはならず、それ以降の足し算も無限大のままなので、結果も無限大になる。
    /// 変数 1024 個の冪集合（2^1024 個）がちょうど最初に溢れる例で、1023 個までなら収まる。
    /// 厳密な値が要るなら <see cref="Zdd.Count"/> を使う。負の値は現れないので、
    /// <see cref="double.NegativeInfinity"/> や <see cref="double.NaN"/> になることはない。
    /// </para>
    /// </remarks>
    public readonly struct ApproximateCardinalityEval : IDdEval<double>
    {
        /// <inheritdoc/>
        public double EvalTerminal(bool isTrue) => isTrue ? 1.0 : 0.0;

        /// <inheritdoc/>
        public double EvalNode(int item, double lo, double hi) => lo + hi;
    }
}

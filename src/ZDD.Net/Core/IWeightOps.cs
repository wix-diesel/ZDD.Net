namespace ZDD.Net.Core
{
    /// <summary>
    /// 重みの型 <typeparamref name="TWeight"/> に「0」「足す」「比べる」の 3 つを与える戦略。
    /// 重み最適化（<see cref="Zdd.MaxWeight{TWeight, TOps}"/> /
    /// <see cref="Zdd.MinWeight{TWeight, TOps}"/> / <see cref="Zdd.TopK{TWeight, TOps}"/>）が使う。
    /// </summary>
    /// <typeparam name="TWeight">重みの型。</typeparam>
    /// <remarks>
    /// <para>
    /// <b>なぜ演算を型で渡すのか</b>: 最適化の DP に要るのは「空集合の重み（＝<see cref="Zero"/>）」
    /// 「item の重みを足す」「2 つの重みを比べる」の 3 つだけである。これを
    /// <see cref="System.IComparable{T}"/> や演算子制約で書くと、有理数・辞書順タプル・
    /// 「重み ＋ タイブレーク用の副次値」のような利用者定義の重みが乗らなくなる。
    /// 戦略を型引数で受け取れば、<c>double</c> でも <c>BigInteger</c> でも、
    /// ライブラリに手を入れずに同じ DP がそのまま動く（docs/OPEN-QUESTIONS.md B10）。
    /// </para>
    /// <para>
    /// <b><c>static abstract</c> である理由</b>: 演算は型ごとに 1 通りしかなく、
    /// インスタンスの状態を要しない。net10 では静的メンバをインタフェースに置けるので、
    /// 「演算を渡すためだけのダミーのインスタンス」を作らずに済む（docs/PLAN.md §2）。
    /// 呼び出し側は <c>TOps.Add(a, b)</c> のように<b>型引数に対して直に</b>呼ぶ。
    /// </para>
    /// <para>
    /// <b>実装は必ず <c>struct</c> にする</b>（docs/PLAN.md §10-2）。この型は
    /// <see cref="IDdEval{TValue}"/> と同じく <b>interface 型では受け取らない</b>。
    /// 公開 API の制約は <c>where TOps : struct, IWeightOps&lt;TWeight&gt;</c> なので、
    /// JIT が実装ごとに専用コードを生成し、ノードごとに何度も走る <see cref="Add"/> /
    /// <see cref="Compare"/> が仮想呼び出しではなく直接呼び出し（多くはインライン展開）になる。
    /// </para>
    /// <para>
    /// <b>実装に求める約束</b>:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="Compare"/> は全順序であること（反対称・推移的）。DP は「より良い方」を
    /// 選び続けるだけなので、順序が壊れていれば答も壊れる。
    /// </description></item>
    /// <item><description>
    /// <see cref="Add"/> は結合的で、<see cref="Zero"/> が単位元であること
    /// （<c>Add(Zero, x) == x</c>）。集合の重みは要素の重みを足し合わせたものなので、
    /// 足す順序で答が変わってはならない。
    /// </description></item>
    /// <item><description>
    /// 交換法則までは要らない（辞書順タプルのような非可換な重みも扱える）が、
    /// 実装が非可換なら「item index の小さい順に足される」ことに依存してよい。
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// 分子・分母を持つ有理数を重みにする例:
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
        /// <summary>加法の単位元。空集合の重みでもある。</summary>
        static abstract TWeight Zero { get; }

        /// <summary>2 つの重みを足す。</summary>
        /// <param name="left">左の重み。</param>
        /// <param name="right">右の重み。</param>
        static abstract TWeight Add(TWeight left, TWeight right);

        /// <summary>2 つの重みを比べる。</summary>
        /// <param name="left">左の重み。</param>
        /// <param name="right">右の重み。</param>
        /// <returns>
        /// <paramref name="left"/> が小さければ負、等しければ 0、大きければ正
        /// （<see cref="System.Collections.Generic.IComparer{T}.Compare"/> と同じ約束）。
        /// </returns>
        static abstract int Compare(TWeight left, TWeight right);
    }
}

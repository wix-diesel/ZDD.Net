namespace ZDD.Net.Core
{
    /// <summary>
    /// 演算キャッシュ（<see cref="OperationCache"/>）のエントリを識別する演算の種別。
    /// キャッシュは 1 本の表を全演算で共有するため、この値がキーの一部になる。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>値を変えてはならない理由は無い</b>: キャッシュはプロセス内の一時表なので、
    /// 永続化された値ではない。ただし <see cref="None"/> = 0 だけは「空きエントリ」の番兵として
    /// <see cref="OperationCache"/> が使うため固定する。
    /// </para>
    /// <para>
    /// ここには M1-5 〜 M1-10 で実装する演算をあらかじめ並べてある。演算そのものは
    /// 後続の PR で入るので、この時点で使われるのは <see cref="OperationCache"/> のテストだけ。
    /// </para>
    /// </remarks>
    internal enum ZddOperation
    {
        /// <summary>演算ではない。空きエントリを表す番兵としてのみ使う。</summary>
        None = 0,

        // ---- 二項演算 ----

        /// <summary>和 <c>f ∪ g</c>（M1-7）。</summary>
        Union,

        /// <summary>積 <c>f ∩ g</c>（M1-7）。</summary>
        Intersect,

        /// <summary>差 <c>f ∖ g</c>（M1-7）。</summary>
        Difference,

        /// <summary>対称差 <c>f ⊕ g</c>（M1-7）。</summary>
        SymmetricDifference,

        /// <summary>族の積 <c>f * g</c>（M1-8）。</summary>
        Product,

        /// <summary>商 <c>f / g</c>（M1-8）。</summary>
        Quotient,

        /// <summary>剰余 <c>f % g</c>（M1-8）。</summary>
        Remainder,

        /// <summary>両者の要素の共通部分から成る族 <c>f ⊓ g</c>（M1-9）。</summary>
        Meet,

        /// <summary><c>g</c> のいずれかを含む要素だけを残す（M1-9）。</summary>
        SupersetsOf,

        /// <summary><c>g</c> のいずれかに含まれる要素だけを残す（M1-9）。</summary>
        SubsetsOf,

        /// <summary><c>g</c> のどれの部分集合でもない要素だけを残す（M1-9）。</summary>
        NonSubsetsOf,

        /// <summary><c>g</c> のどれの上位集合でもない要素だけを残す（M1-9）。</summary>
        NonSupersetsOf,

        // ---- 単項演算（item を取るもの・取らないもの） ----

        /// <summary>各要素の <c>item</c> の有無を反転する（M1-5）。</summary>
        Change,

        /// <summary><c>item</c> を含む要素を選び、そこから <c>item</c> を除く（M1-5）。</summary>
        OnSet,

        /// <summary><c>item</c> を含まない要素だけを残す（M1-5）。</summary>
        OffSet,

        /// <summary>包含関係で極大な要素だけを残す（M1-10）。</summary>
        Maximal,

        /// <summary>包含関係で極小な要素だけを残す（M1-10）。</summary>
        Minimal,

        /// <summary>ヒッティング集合の族（M1-10）。</summary>
        HittingSets,

        /// <summary>補（<c>2^V ∖ f</c>）（M1-10）。</summary>
        Complement,
    }

    /// <summary>
    /// <see cref="ZddOperation"/> の性質を問い合わせる述語。キャッシュのキー生成
    /// （可換演算のオペランド正規化）と、引数の取り違えを Debug ビルドで捕まえるために使う。
    /// </summary>
    internal static class ZddOperations
    {
        /// <summary>
        /// オペランドを入れ替えても結果が変わらない二項演算かどうか。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 可換な演算では <c>(op, f, g)</c> と <c>(op, g, f)</c> を同じキーに正規化できる。
        /// 二項演算の再帰は左右のオペランドがしばしば入れ替わった形で同じ部分問題に到達するため、
        /// この正規化だけでヒット率が実質的に上がる。
        /// </para>
        /// <para>
        /// 数学的に可換でも、<b>実装の分解が本当に対称</b>でなければここに入れてはならない
        /// （誤って可換とみなすと<b>誤った結果を返す</b>ので、疑わしいものは非可換側に置く）。
        /// <see cref="ZddOperation.Product"/> は M1-8 で対称であることを確かめて加えた:
        /// レベルが揃った分解は <c>f₀ * g₀</c> と 3 項の和のどちらも左右の入れ替えで不変で、
        /// 片方だけが上にある分解も「上の族を降ろす」形が同じになる。
        /// <see cref="ZddOperation.Quotient"/> / <see cref="ZddOperation.Remainder"/> は
        /// そもそも非可換（<c>f / g</c> と <c>g / f</c> は別物）。
        /// </para>
        /// </remarks>
        public static bool IsCommutative(ZddOperation op) =>
            op is ZddOperation.Union
                or ZddOperation.Intersect
                or ZddOperation.SymmetricDifference
                or ZddOperation.Product
                or ZddOperation.Meet;

        /// <summary>
        /// 単項演算（第 2 オペランドが別の族ではなく item、あるいは何も取らないもの）かどうか。
        /// </summary>
        public static bool IsUnary(ZddOperation op) =>
            op is ZddOperation.Change
                or ZddOperation.OnSet
                or ZddOperation.OffSet
                or ZddOperation.Maximal
                or ZddOperation.Minimal
                or ZddOperation.HittingSets
                or ZddOperation.Complement;
    }
}

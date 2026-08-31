using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 集合の族（family of sets）を表す値型ハンドル。所有する <see cref="ZddManager"/> への参照と
    /// ノード ID だけを持ち、大きさは 16 バイト。族の実体はマネージャ側のノード表にある。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>値型である理由</b>: 族は演算のたびに大量に生まれるので、ハンドルがクラスだと
    /// 演算 1 回ごとにヒープ割り当てが発生する。マネージャ参照を持たせているのは、
    /// <c>a | b</c> のような演算子が書ける・別マネージャの族を混ぜた誤用を検出できるため
    /// （docs/OPEN-QUESTIONS.md B4）。
    /// </para>
    /// <para>
    /// <b>等値</b>: ZDD は正準形なので「族が等しい ⇔ ノード ID が等しい」が成り立つ。
    /// よって等値比較は所有マネージャの参照一致とノード ID の一致だけで、族の走査は要らない。
    /// 別のマネージャで作った同じ内容の族は<b>等しくない</b>（ノード ID が別物のため）。
    /// </para>
    /// <para>
    /// <b><c>default(Zdd)</c></b>: どのマネージャにも属さない無効なハンドルで、
    /// <see cref="IsDefault"/> が <see langword="true"/> を返す。族としての操作は
    /// <see cref="InvalidOperationException"/> になる。等値比較と <see cref="GetHashCode"/> だけは
    /// 例外を投げずに使える（コレクションに入れても壊れないようにするため）。
    /// </para>
    /// </remarks>
    public readonly struct Zdd : IEquatable<Zdd>, IEnumerable<int[]>
    {
        private readonly ZddManager? _manager;
        private readonly int _id;

        internal Zdd(ZddManager manager, int id)
        {
            _manager = manager;
            _id = id;
        }

        /// <summary>この族を所有するマネージャ。</summary>
        /// <exception cref="InvalidOperationException">
        /// <c>default(Zdd)</c> の場合（どのマネージャにも属さないため）。
        /// </exception>
        public ZddManager Manager
        {
            get
            {
                EnsureNotDefault();
                return _manager!;
            }
        }

        /// <summary>
        /// <c>default(Zdd)</c>（どのマネージャにも属さない無効なハンドル）かどうか。
        /// </summary>
        public bool IsDefault => _manager is null;

        /// <summary>この族が空の族 ∅ かどうか。</summary>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        public bool IsEmpty
        {
            get
            {
                EnsureNotDefault();
                return _id == NodeTable.Bottom;
            }
        }

        /// <summary>この族が <c>{∅}</c>（空集合だけを持つ族）かどうか。</summary>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        public bool IsBase
        {
            get
            {
                EnsureNotDefault();
                return _id == NodeTable.Top;
            }
        }

        /// <summary>
        /// この族の根から到達できる非終端ノードの個数。終端 ⊥ / ⊤ は数えないので、
        /// <see cref="ZddManager.Empty"/> と <see cref="ZddManager.Base"/> はともに 0 になる。
        /// </summary>
        /// <remarks>
        /// 呼ぶたびに族を走査する（<see cref="ZddManager.NodeCount"/> と違い、キャッシュした値ではない）。
        /// 走査は明示スタックで、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public long NodeCount => Manager.CountReachableNodes(_id);

        /// <summary>
        /// この族が実際に使っている item（変数）を昇順で返す。
        /// 族の記述に一度も現れない item は含まれない。
        /// </summary>
        /// <returns>
        /// item index の昇順配列。呼び出しごとに新しい配列を返すので、書き換えても族には影響しない。
        /// 終端だけの族（∅ と <c>{∅}</c>）では空配列。
        /// </returns>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public int[] Support() => Manager.CollectSupport(_id);

        /// <summary>
        /// この族に属する集合の個数（濃度）。厳密な値を返す。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 濃度は変数の個数に対して指数的に大きくなりうる（n 変数の冪集合なら 2^n）ので、
        /// 型は <see cref="BigInteger"/> にしてある。数え上げ自体は
        /// <b>ノード数ぶんの足し算</b>で済むので、10^24 個の集合を持つ族でも一瞬で返る
        /// （<see cref="CardinalityEval"/>）。
        /// </para>
        /// <para>
        /// 呼ぶたびに族を走査する（値は覚えておかない）。速さが要るなら
        /// <see cref="CountApprox"/> を使う。走査は明示スタックによる反復で、再帰しない
        /// （docs/PLAN.md §4.5）。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public BigInteger Count => this.Evaluate<CardinalityEval, BigInteger>(default);

        /// <summary>
        /// この族に属する集合の個数を <see cref="double"/> で近似した値。<see cref="Count"/> より速い。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 濃度が 2^53 以下なら <see cref="Count"/> と<b>厳密に一致</b>する。それを超えると
        /// 下位の桁が丸められ、<see cref="double.MaxValue"/>（およそ 1.8 × 10^308）を超えると
        /// <see cref="double.PositiveInfinity"/> になる（例外にはならない）。
        /// 詳しくは <see cref="ApproximateCardinalityEval"/> を参照。
        /// </para>
        /// <para>
        /// 走査の形は <see cref="Count"/> と同じで、違うのは足し算の型だけである
        /// （<see cref="BigInteger"/> の加算は桁数に比例した時間とアロケーションを伴う。
        /// docs/PLAN.md §10-5）。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public double CountApprox => this.Evaluate<ApproximateCardinalityEval, double>(default);

        /// <summary>
        /// この族に属する集合の個数を、集合の要素数ごとに数えた分布を返す。
        /// </summary>
        /// <returns>
        /// 添字 <c>k</c> に「要素数 <c>k</c> の集合の個数」が入った配列。長さは
        /// <b>この族に属する集合の最大要素数 + 1</b>で、空の族 ∅ では長さ 0、
        /// <c>{∅}</c> では <c>[1]</c> になる。総和は <see cref="Count"/> に一致する。
        /// 呼び出しごとに新しい配列を返すので、書き換えても族には影響しない。
        /// </returns>
        /// <remarks>
        /// 冪集合なら二項係数の並び（<c>[C(n,0), C(n,1), …, C(n,n)]</c>）になる。
        /// ノードごとに配列を 1 本作るため、時間・メモリとも
        /// <c>O(ノード数 × 最大要素数)</c> かかる（<see cref="SizeDistributionEval"/>）。
        /// 総数だけが要るなら <see cref="Count"/> のほうが桁違いに軽い。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public BigInteger[] CountBySize() => this.Evaluate<SizeDistributionEval, BigInteger[]>(default);

        /// <summary>
        /// 和 <c>F ∪ G</c>。どちらか一方にでも属する集合を持つ族を返す。
        /// </summary>
        /// <param name="g">相手の族。この族と同じマネージャに属していなければならない。</param>
        /// <remarks>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// 途中結果はマネージャの演算キャッシュに載るので、同じ組合せを繰り返しても安い。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Union(Zdd g) => Manager.Union(this, g);

        /// <summary>
        /// 積 <c>F ∩ G</c>。両方に属する集合だけを持つ族を返す。
        /// </summary>
        /// <param name="g">相手の族。この族と同じマネージャに属していなければならない。</param>
        /// <remarks>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Intersect(Zdd g) => Manager.Intersect(this, g);

        /// <summary>
        /// 差 <c>F ∖ G</c>。この族のうち <paramref name="g"/> に属さない集合だけを返す。
        /// </summary>
        /// <param name="g">相手の族。この族と同じマネージャに属していなければならない。</param>
        /// <remarks>
        /// 集合ごとの差ではなく<b>族としての差</b>である（集合 <c>{0, 1}</c> から <c>{0}</c> を
        /// 引くような操作ではない）。実装は明示スタックによる反復で、再帰しない。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Difference(Zdd g) => Manager.Difference(this, g);

        /// <summary>
        /// 対称差 <c>F △ G</c>。ちょうど一方にだけ属する集合を持つ族を返す。
        /// </summary>
        /// <param name="g">相手の族。この族と同じマネージャに属していなければならない。</param>
        /// <remarks>
        /// <c>(F ∪ G) ∖ (F ∩ G)</c> と同じ族だが、こちらは 1 回の走査で求める。
        /// 実装は明示スタックによる反復で、再帰しない。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd SymmetricDifference(Zdd g) => Manager.SymmetricDifference(this, g);

        /// <summary>
        /// 積 <c>F * G</c>。両方から 1 つずつ集合を採り、その和を集めた族
        /// <c>{ a ∪ b : a ∈ F, b ∈ G }</c> を返す（直積結合・join）。
        /// </summary>
        /// <param name="g">相手の族。この族と同じマネージャに属していなければならない。</param>
        /// <returns>
        /// 集合の個数は掛け算にならない。<c>a ∪ b</c> が同じになる組は 1 つに潰れるので、
        /// 結果は高々 <c>|F| × |G|</c> 個である。
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>境界的な入力</b>: <c>F * {∅} == F</c>（<c>{∅}</c> が単位元）、
        /// <c>F * ∅ == ∅</c>（相手が 1 つも集合を持たないので、作れる和も無い）。
        /// </para>
        /// <para>
        /// 交換則・結合則が成り立ち、<see cref="Union"/> に対して分配する。
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Product(Zdd g) => Manager.Product(this, g);

        /// <summary>
        /// 商 <c>F / G</c>。<c>G</c> のどの集合とも重ならず、どれと足しても <c>F</c> に入る集合
        /// <c>{ a : ∀ b ∈ G, a ∩ b = ∅ かつ a ∪ b ∈ F }</c> を返す。
        /// </summary>
        /// <param name="g">割る族。この族と同じマネージャに属していなければならない。</param>
        /// <returns>
        /// <c>F</c> から <c>G</c> を「くくり出した」残りの族。<c>F / G * G</c> は <c>F</c> の部分族で、
        /// くくり出せなかったぶんが <see cref="Remainder"/> になる。
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>境界的な入力</b>:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <c>F / {∅} == F</c>。<c>a ∪ ∅ = a</c> なので、条件は「<c>a ∈ F</c>」だけになる。
        /// </description></item>
        /// <item><description>
        /// <c>F / ∅</c> は<b>全体集合の冪集合 2^U</b>（<see cref="ZddManager.VariableCount"/> 個の
        /// item の全部分集合）。「∀ b ∈ ∅」は空虚に真なので、定義どおりならすべての部分集合が商に入る。
        /// エラーにする流儀もあるが、ここでは定義に従う。<c>F % ∅ == F</c> と合わせて
        /// <c>F == F / G * G + F % G</c> は保たれる（<c>2^U * ∅ == ∅</c> のため）。
        /// </description></item>
        /// <item><description>
        /// <c>∅ / G == ∅</c>、および <c>F / F == {∅}</c>（<c>F</c> が ∅ でないとき）。
        /// </description></item>
        /// </list>
        /// <para>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Quotient(Zdd g) => Manager.Quotient(this, g);

        /// <summary>
        /// 剰余 <c>F % G</c>。<c>F ∖ (G * (F / G))</c>、すなわち <c>G</c> でくくり出せなかった集合を返す。
        /// </summary>
        /// <param name="g">割る族。この族と同じマネージャに属していなければならない。</param>
        /// <returns>
        /// <c>F == F / G * G + F % G</c>（<c>+</c> は <see cref="Union"/>）を満たす族。
        /// </returns>
        /// <remarks>
        /// <b>境界的な入力</b>: <c>F % {∅} == ∅</c>（<c>F / {∅} * {∅} == F</c> なので割り切れる）、
        /// <c>F % ∅ == F</c>（商が何であれ ∅ を掛ければ ∅ なので、何も引かれない）。
        /// 実装は商・積・差の組み合わせで、いずれも反復実装であり再帰しない。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Remainder(Zdd g) => Manager.Remainder(this, g);

        /// <summary>
        /// Meet <c>F ⊓ G</c>。両方から 1 つずつ集合を採り、その<b>共通部分</b>を集めた族
        /// <c>{ a ∩ b : a ∈ F, b ∈ G }</c> を返す。
        /// </summary>
        /// <param name="g">相手の族。この族と同じマネージャに属していなければならない。</param>
        /// <remarks>
        /// <para>
        /// <see cref="Product"/> の「和を集める」を「交わりを集める」に替えたもの。
        /// 交換則・結合則が成り立ち、<see cref="Union"/> に対して分配する。
        /// </para>
        /// <para>
        /// <b>境界的な入力</b>: <c>F ⊓ ∅ == ∅</c>（相手が 1 つも集合を持たないので、作れる交わりも無い）、
        /// <c>F ⊓ {∅} == {∅}</c>（∅ との交わりは常に ∅ なので、できるのは 1 通りだけ）。
        /// <c>F ⊓ F</c> は <c>F</c> とは限らない（要素どうしの交わりが新しく増える）。
        /// </para>
        /// <para>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Meet(Zdd g) => Manager.Meet(this, g);

        /// <summary>
        /// <paramref name="g"/> のいずれかを<b>含む</b>集合だけを残す
        /// （<c>{ a ∈ F : ∃ b ∈ G, b ⊆ a }</c>）。
        /// </summary>
        /// <param name="g">条件を与える族。この族と同じマネージャに属していなければならない。</param>
        /// <remarks>
        /// <para>
        /// <b>名前について</b>: SAPPOROBDD 由来の名前が <see cref="Restrict"/>、
        /// 何が残るかをそのまま言い表した .NET 的な名前がこちら。<b>同じ演算</b>で、
        /// どちらの名前で探しても見つかるように両方を用意してある。
        /// </para>
        /// <para>
        /// 構築済みの巨大な族を後から絞り込む主要手段で、「全域木のうち、この辺集合を含むもの」の
        /// ように使う。集合そのものは作り替えないので、結果は必ず <c>F</c> の部分族になる。
        /// </para>
        /// <para>
        /// <b>境界的な入力</b>: <c>F.SupersetsOf(Base) == F</c>（∅ はどの集合にも含まれる）、
        /// <c>F.SupersetsOf(∅) == ∅</c>（条件を満たす <c>b</c> が 1 つも無い）。
        /// </para>
        /// <para>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd SupersetsOf(Zdd g) => Manager.SupersetsOf(this, g);

        /// <summary><see cref="SupersetsOf"/> の別名（SAPPOROBDD の記法）。同じ演算を指す。</summary>
        /// <param name="g">条件を与える族。この族と同じマネージャに属していなければならない。</param>
        public Zdd Restrict(Zdd g) => Manager.SupersetsOf(this, g);

        /// <summary>
        /// <paramref name="g"/> のいずれかに<b>含まれる</b>集合だけを残す
        /// （<c>{ a ∈ F : ∃ b ∈ G, a ⊆ b }</c>）。
        /// </summary>
        /// <param name="g">条件を与える族。この族と同じマネージャに属していなければならない。</param>
        /// <remarks>
        /// <para>
        /// <b>名前について</b>: SAPPOROBDD 由来の名前が <see cref="Permit"/>、
        /// 何が残るかをそのまま言い表した .NET 的な名前がこちら。<b>同じ演算</b>で、
        /// どちらの名前で探しても見つかるように両方を用意してある。
        /// </para>
        /// <para>
        /// 「パスのうち、使ってよい辺だけでできているもの」のように、許可された集合の範囲へ
        /// 族を閉じ込めるのに使う。結果は必ず <c>F</c> の部分族になる。
        /// </para>
        /// <para>
        /// <b>境界的な入力</b>: <c>F.SubsetsOf(∅) == ∅</c>、
        /// <c>F.SubsetsOf(Base)</c> は <c>F</c> が空集合を含むなら <c>{∅}</c>、含まなければ ∅
        /// （<c>a ⊆ ∅</c> を満たすのは <c>a = ∅</c> だけ）。
        /// </para>
        /// <para>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd SubsetsOf(Zdd g) => Manager.SubsetsOf(this, g);

        /// <summary><see cref="SubsetsOf"/> の別名（SAPPOROBDD の記法）。同じ演算を指す。</summary>
        /// <param name="g">条件を与える族。この族と同じマネージャに属していなければならない。</param>
        public Zdd Permit(Zdd g) => Manager.SubsetsOf(this, g);

        /// <summary>
        /// <paramref name="g"/> のどれの<b>部分集合でもない</b>集合だけを残す
        /// （<c>{ a ∈ F : ∀ b ∈ G, a ⊄ b }</c>）。
        /// </summary>
        /// <param name="g">条件を与える族。この族と同じマネージャに属していなければならない。</param>
        /// <returns>
        /// <see cref="SubsetsOf"/> の否定版で、<c>F.NonSubsetsOf(G) == F - F.SubsetsOf(G)</c> が成り立つ。
        /// 差を取らずに 1 回の走査で求めるので、中間の族を作らずに済む。
        /// </returns>
        /// <remarks>
        /// <b>境界的な入力</b>: <c>F.NonSubsetsOf(∅) == F</c>（「∀ b ∈ ∅」は空虚に真）、
        /// <c>F.NonSubsetsOf(F) == ∅</c>。実装は明示スタックによる反復で、再帰しない。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd NonSubsetsOf(Zdd g) => Manager.NonSubsetsOf(this, g);

        /// <summary>
        /// <paramref name="g"/> のどれの<b>上位集合でもない</b>集合だけを残す
        /// （<c>{ a ∈ F : ∀ b ∈ G, b ⊄ a }</c>）。
        /// </summary>
        /// <param name="g">条件を与える族。この族と同じマネージャに属していなければならない。</param>
        /// <returns>
        /// <see cref="SupersetsOf"/> の否定版で、<c>F.NonSupersetsOf(G) == F - F.SupersetsOf(G)</c> が
        /// 成り立つ。「この辺集合を 1 つも丸ごとは含まない解」を取り出すのに使う。
        /// </returns>
        /// <remarks>
        /// <b>境界的な入力</b>: <c>F.NonSupersetsOf(∅) == F</c>、
        /// <c>F.NonSupersetsOf(Base) == ∅</c>（∅ はどの集合にも含まれてしまう）。
        /// 実装は明示スタックによる反復で、再帰しない。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd NonSupersetsOf(Zdd g) => Manager.NonSupersetsOf(this, g);

        /// <summary>
        /// この族のすべての集合について、<paramref name="item"/> の有無を反転した族を返す。
        /// </summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        /// <returns>
        /// <c>{ s △ {item} : s ∈ this }</c>。たとえば <c>{∅, {1}}</c> に <c>Change(1)</c> をかけると
        /// <c>{{1}, ∅}</c>、すなわち同じ族に戻る。<b>集合の個数は変わらない</b>ので、
        /// <c>Change(i)</c> を 2 回かければ必ず元の族になる。
        /// </returns>
        /// <remarks>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// 途中結果はマネージャの演算キャッシュに載るので、同じ族に同じ演算を繰り返しても安い。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> が範囲外の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Change(int item) => Manager.Change(this, item);

        /// <summary>
        /// <paramref name="item"/> を含む集合だけを取り出し、そこから <paramref name="item"/> を
        /// 除いた族を返す（Minato の <c>Subset1</c>）。
        /// </summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        /// <returns>
        /// <c>{ s ∖ {item} : s ∈ this, item ∈ s }</c>。<see cref="OffSet"/> と対になっていて、
        /// <c>OffSet(i)</c> と <c>OnSet(i).Change(i)</c> は元の族を重複なく 2 つに分ける。
        /// </returns>
        /// <remarks>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> が範囲外の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd OnSet(int item) => Manager.OnSet(this, item);

        /// <summary><see cref="OnSet"/> の別名（Minato の記法）。</summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        public Zdd Subset1(int item) => Manager.OnSet(this, item);

        /// <summary>
        /// <paramref name="item"/> を含まない集合だけを残した族を返す（Minato の <c>Subset0</c>）。
        /// </summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        /// <returns><c>{ s : s ∈ this, item ∉ s }</c>。集合そのものは変わらない。</returns>
        /// <remarks>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> が範囲外の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd OffSet(int item) => Manager.OffSet(this, item);

        /// <summary><see cref="OffSet"/> の別名（Minato の記法）。</summary>
        /// <param name="item">0 以上 <see cref="ZddManager.VariableCount"/> 未満の item index。</param>
        public Zdd Subset0(int item) => Manager.OffSet(this, item);

        /// <summary>
        /// この族のすべての集合について、<paramref name="items"/> の有無をまとめて反転した族を返す
        /// （<see cref="Change"/> の一般化）。
        /// </summary>
        /// <param name="items">
        /// 反転する item index の並び。それぞれ 0 以上 <see cref="ZddManager.VariableCount"/> 未満。
        /// 空なら族はそのまま返る。
        /// </param>
        /// <returns>
        /// <c>{ s △ items : s ∈ this }</c>。<b>集合の個数は変わらない</b>。
        /// 同じ item を 2 度渡すと反転が 2 回かかって打ち消し合うので、その item は元のままになる。
        /// </returns>
        /// <remarks>
        /// <see cref="Change"/> を順に掛けるだけで、item どうしの順序は結果に影響しない。
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="items"/> に範囲外の item がある場合（族は 1 つも反転されない）。
        /// </exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Flip(params ReadOnlySpan<int> items) => Manager.Flip(this, items);

        /// <summary>
        /// 包含関係で<b>極大</b>な集合だけを残した族を返す
        /// （<c>{ a ∈ F : a ⊊ b となる b ∈ F が無い }</c>）。
        /// </summary>
        /// <returns>
        /// 元の族の部分族で、必ず<b>反鎖</b>（どの 2 つも包含関係にない）になる。
        /// したがって <c>F.Maximal().Maximal() == F.Maximal()</c>。
        /// </returns>
        /// <remarks>
        /// <b>境界的な入力</b>: <c>∅.Maximal() == ∅</c>、<c>{∅}.Maximal() == {∅}</c>。
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Maximal() => Manager.Maximal(this);

        /// <summary>
        /// 包含関係で<b>極小</b>な集合だけを残した族を返す
        /// （<c>{ a ∈ F : b ⊊ a となる b ∈ F が無い }</c>）。
        /// </summary>
        /// <returns>
        /// 元の族の部分族で、必ず<b>反鎖</b>（どの 2 つも包含関係にない）になる。
        /// したがって <c>F.Minimal().Minimal() == F.Minimal()</c>。
        /// </returns>
        /// <remarks>
        /// 「冗長な解を落とす」定番の操作で、極小カットや極小頂点被覆を取り出すのに使う。
        /// <b>境界的な入力</b>: <c>∅.Minimal() == ∅</c>、<c>{∅}.Minimal() == {∅}</c>。
        /// <c>F</c> が空集合を持つなら、<c>F.Minimal() == {∅}</c>（∅ はどの集合にも真に含まれる）。
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Minimal() => Manager.Minimal(this);

        /// <summary>
        /// この族のどの集合とも交わる集合をすべて集めた族（ブロッキング集合族／横断超グラフ）を返す
        /// （<c>{ a ⊆ U : ∀ b ∈ F, a ∩ b ≠ ∅ }</c>）。
        /// </summary>
        /// <returns>
        /// 全体集合 <c>U</c> は所有マネージャの<b>全変数</b>（<see cref="ZddManager.VariableCount"/>）で、
        /// <see cref="Support"/> ではない。この族が使っていない item も候補に自由に入れてよいため。
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b>極小なものだけが要るなら <c>HittingSets().Minimal()</c></b> と書く。この演算が返すのは
        /// 「交わる集合すべて」なので、上位集合もすべて含んだ上に閉じた族になる。
        /// 反鎖どうしの双対（Berge の定理）は極小化を挟んで
        /// <c>F.Minimal().HittingSets().Minimal().HittingSets().Minimal() == F.Minimal()</c> の形になる。
        /// </para>
        /// <para>
        /// <b>結果が指数的に大きくなりうる</b>。横断超グラフの大きさは元の族に対して指数的になりうるので、
        /// 大きな族に無条件で掛けてよい演算ではない。
        /// </para>
        /// <para>
        /// <b>境界的な入力</b>: <c>∅.HittingSets() == 2^U</c>（条件が空虚に真）、
        /// <c>{∅}.HittingSets() == ∅</c>（∅ と交われる集合は無い）。空集合を含む族はすべて後者になる。
        /// </para>
        /// <para>
        /// 実装は明示スタックによる反復で、再帰しない（docs/PLAN.md §4.5）。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd HittingSets() => Manager.HittingSets(this);

        /// <summary><see cref="HittingSets"/> の別名（ブロッキング集合族）。同じ演算を指す。</summary>
        public Zdd Blocking() => Manager.HittingSets(this);

        /// <summary>
        /// 補 <c>2^U ∖ F</c>。全体集合 <c>U</c> の部分集合のうち、この族に属さないものを集めた族を返す。
        /// </summary>
        /// <returns>
        /// 全体集合 <c>U</c> は所有マネージャの<b>全変数</b>（<see cref="ZddManager.VariableCount"/>）で、
        /// <see cref="Support"/> ではない（docs/OPEN-QUESTIONS.md B8）。したがって同じ内容の族でも、
        /// 変数の個数が違うマネージャでは補が違う。一部の item だけを全体集合と見る補
        /// （<c>ComplementWithin(items)</c>）は別の API として用意する予定である。
        /// </returns>
        /// <remarks>
        /// <b>集合ごとの補ではなく族としての補</b>である（各集合を <c>U ∖ s</c> に置き換える操作ではない）。
        /// <c>~~F == F</c>、<c>~∅ == 2^U</c>、<c>~2^U == ∅</c>。
        /// <see cref="Union"/> / <see cref="Intersect"/> との間にド・モルガン則
        /// （<c>~(F ∪ G) == ~F ∩ ~G</c>、<c>~(F ∩ G) == ~F ∪ ~G</c>）が成り立つ。
        /// 実装は冪集合との <see cref="Difference"/> で、反復であり再帰しない。
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public Zdd Complement() => Manager.Complement(this);

        /// <summary>
        /// この族に属する集合を 1 つずつ返す遅延列挙を始める。順序は
        /// <see cref="ZddEnumerationOrder.Default"/>。
        /// </summary>
        /// <returns>
        /// 族に属する集合の列挙子。集合は<b>昇順に並んだ item index の <c>int[]</c></b> で、
        /// <b>1 つ返すたびに新しい配列</b>が作られる（<see cref="Sets"/> を参照）。
        /// </returns>
        /// <remarks>
        /// <para>
        /// <b><see cref="Count"/> と計算量が違う</b>。数え上げはノード数に比例するので 10^24 個でも
        /// 一瞬だが、列挙は<b>返す集合の個数</b>に比例する。だからこそ遅延で、
        /// <c>foreach</c> の途中で <c>break</c> したり <c>Take(10)</c> で打ち切ったりすれば、
        /// 族がどれだけ大きくてもそこまでしか辿らない。
        /// </para>
        /// <para>
        /// <b><see cref="System.Collections.Generic.ICollection{T}"/> は実装しない</b>
        /// （docs/PLAN.md §8）。族の要素数は <c>int</c> に収まらないためで、個数が要るときは
        /// LINQ の <c>Count()</c> ではなく <see cref="Count"/>（<see cref="BigInteger"/>）を使う。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public IEnumerator<int[]> GetEnumerator() => Sets().GetEnumerator();

        /// <inheritdoc cref="GetEnumerator"/>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// この族に属する集合を、指定した順序で 1 つずつ返す遅延列挙を作る。
        /// </summary>
        /// <param name="order">
        /// 集合を返す順序。既定は <see cref="ZddEnumerationOrder.Default"/>
        /// （0-枝優先の深さ優先＝指示ベクトルの辞書順）。
        /// </param>
        /// <returns>族に属する集合を <paramref name="order"/> の順に返す遅延列挙。</returns>
        /// <remarks>
        /// <para>
        /// <b>返る配列は毎回新しい</b>。バッファを使い回すと <c>ToList()</c> した全要素が
        /// 同じ配列を指すという静かな罠になるので、既定は安全側に倒してある。
        /// 返された <c>int[]</c> は呼び出し側のもので、書き換えても列挙には影響しない。
        /// </para>
        /// <para>
        /// <b>遅延である</b>。ここでは何も辿らず、列挙が進むたびに 1 つ分だけ走査する
        /// （引数の検査だけはこの場で行う）。同じ戻り値を 2 度 <c>foreach</c> すれば 2 度走査され、
        /// 族は不変なので同じ並びが 2 度返る。
        /// </para>
        /// <para>
        /// <b>計算量</b>: 集合 1 つあたり、その集合の要素数と辿った 0-枝のぶん。
        /// 族全体を列挙する手間は「集合の個数 × 変数の個数」で抑えられる。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="order"/> が定義されていない値の場合。
        /// </exception>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public IEnumerable<int[]> Sets(ZddEnumerationOrder order = ZddEnumerationOrder.Default) =>
            SetEnumeration.Enumerate(Manager, _id, order);

        /// <summary>
        /// <paramref name="set"/> が表す集合がこの族に属するかどうかを返す。
        /// </summary>
        /// <param name="set">
        /// 調べる集合の item index。順不同でよく、同じ item が重なっていても 1 つとして扱う。
        /// 空なら「この族が空集合を要素に持つか」を問うことになる。
        /// </param>
        /// <remarks>
        /// 族を作らず、根から終端まで 1 本の経路を降りるだけなので O(変数の個数)
        /// （<paramref name="set"/> を昇順に並べるぶん、要素数 k に対して O(k log k) が加わる）。
        /// 列挙と整合する: <see cref="Sets"/> が返した集合は必ず <see langword="true"/> になる。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="set"/> が <see langword="null"/> の場合。</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="set"/> に 0 以上 <see cref="ZddManager.VariableCount"/> 未満でない値が含まれる場合。
        /// </exception>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public bool Contains(IEnumerable<int> set)
        {
            ThrowHelper.ThrowIfNull(set, nameof(set));

            int[] items = set as int[] ?? new List<int>(set).ToArray();
            return Manager.Contains(this, items);
        }

        /// <inheritdoc cref="Contains(IEnumerable{int})"/>
        /// <param name="items">
        /// 調べる集合の item index。順不同でよく、同じ item が重なっていても 1 つとして扱う。
        /// 空なら「この族が空集合を要素に持つか」を問うことになる。
        /// </param>
        public bool Contains(params ReadOnlySpan<int> items) => Manager.Contains(this, items);

        /// <summary>
        /// この族の集合がすべて <paramref name="g"/> にも属するか（族としての包含 <c>F ⊆ G</c>）を返す。
        /// </summary>
        /// <param name="g">相手の族。このマネージャに属していなければならない。</param>
        /// <remarks>
        /// <c>(F - G).IsEmpty</c> と同じ答だが、<b>差の族を組み立てない</b>。
        /// 反例（<c>G</c> に無い <c>F</c> の集合）が 1 つ見つかった時点で打ち切る。
        /// 空の族はどの族にも含まれ（<c>∅.IsSubsetOf(G)</c> は常に真）、
        /// <c>F.IsSubsetOf(F)</c> も常に真。
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public bool IsSubsetOf(Zdd g) => Manager.IsSubsetOf(this, g);

        /// <summary>
        /// この族と <paramref name="g"/> に共通の集合があるかどうかを返す。
        /// </summary>
        /// <param name="g">相手の族。このマネージャに属していなければならない。</param>
        /// <remarks>
        /// <c>(F &amp; G) != Empty</c> と同じ答だが、<b>交わりの族を組み立てない</b>。
        /// 共通の集合が 1 つ見つかった時点で打ち切る。どちらかが空の族なら常に偽。
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="g"/> が別のマネージャに属する、または <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="InvalidOperationException"><c>default(Zdd)</c> の場合。</exception>
        /// <exception cref="ObjectDisposedException">所有マネージャが破棄済みの場合。</exception>
        public bool Overlaps(Zdd g) => Manager.Overlaps(this, g);

        /// <summary>2 つのハンドルが同じマネージャの同じ族を指すかどうか。</summary>
        /// <param name="other">比較相手。</param>
        public bool Equals(Zdd other) => ReferenceEquals(_manager, other._manager) && _id == other._id;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Zdd other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(_manager is null ? 0 : RuntimeHelpers.GetHashCode(_manager), _id);

        /// <summary>2 つのハンドルが同じマネージャの同じ族を指すかどうか。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static bool operator ==(Zdd left, Zdd right) => left.Equals(right);

        /// <summary>2 つのハンドルが異なる族（または異なるマネージャ）を指すかどうか。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static bool operator !=(Zdd left, Zdd right) => !left.Equals(right);

        /// <summary>和 <c>F ∪ G</c>。<see cref="Union"/> と同じ。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static Zdd operator |(Zdd left, Zdd right) => left.Manager.Union(left, right);

        /// <summary>積 <c>F ∩ G</c>。<see cref="Intersect"/> と同じ。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static Zdd operator &(Zdd left, Zdd right) => left.Manager.Intersect(left, right);

        /// <summary>差 <c>F ∖ G</c>。<see cref="Difference"/> と同じ。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static Zdd operator -(Zdd left, Zdd right) => left.Manager.Difference(left, right);

        /// <summary>対称差 <c>F △ G</c>。<see cref="SymmetricDifference"/> と同じ。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static Zdd operator ^(Zdd left, Zdd right) => left.Manager.SymmetricDifference(left, right);

        /// <summary>積 <c>F * G</c>。<see cref="Product"/> と同じ。</summary>
        /// <param name="left">左辺。</param>
        /// <param name="right">右辺。</param>
        public static Zdd operator *(Zdd left, Zdd right) => left.Manager.Product(left, right);

        /// <summary>商 <c>F / G</c>。<see cref="Quotient"/> と同じ。</summary>
        /// <param name="left">割られる族。</param>
        /// <param name="right">割る族。</param>
        public static Zdd operator /(Zdd left, Zdd right) => left.Manager.Quotient(left, right);

        /// <summary>剰余 <c>F % G</c>。<see cref="Remainder"/> と同じ。</summary>
        /// <param name="left">割られる族。</param>
        /// <param name="right">割る族。</param>
        public static Zdd operator %(Zdd left, Zdd right) => left.Manager.Remainder(left, right);

        /// <summary>補 <c>2^U ∖ F</c>。<see cref="Complement"/> と同じ。</summary>
        /// <param name="operand">補を取る族。</param>
        public static Zdd operator ~(Zdd operand) => operand.Manager.Complement(operand);

        /// <summary>デバッグ用の短い表現。族の中身は展開しない。</summary>
        public override string ToString()
        {
            if (_manager is null)
            {
                return "Zdd(default)";
            }

            return _id switch
            {
                NodeTable.Bottom => "Zdd(empty)",
                NodeTable.Top => "Zdd(base)",
                _ => $"Zdd(#{_id})",
            };
        }

        /// <summary>所有マネージャ。<c>default(Zdd)</c> なら <see langword="null"/>。</summary>
        internal ZddManager? Owner => _manager;

        /// <summary>この族の根のノード ID。<c>default(Zdd)</c> では意味を持たない。</summary>
        internal int Id => _id;

        private void EnsureNotDefault()
        {
            if (_manager is null)
            {
                ThrowHelper.ThrowInvalidOperationException(
                    "This is a default Zdd handle, which does not belong to any manager. Obtain a Zdd from a ZddManager instead.");
            }
        }
    }
}

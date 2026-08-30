using System;
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
    public readonly struct Zdd : IEquatable<Zdd>
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

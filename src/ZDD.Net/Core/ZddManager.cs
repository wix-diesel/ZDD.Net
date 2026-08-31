using System;
using System.Collections.Generic;
using System.Numerics;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// ZDD の生成と所有を担うマネージャ。ノード表・一意化表を抱え、
    /// <see cref="Zdd"/> ハンドルはすべてこのインスタンスに属する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>item と level</b>: 利用者が扱うのは 0 始まりの <i>item index</i>（0 … <see cref="VariableCount"/> - 1）で、
    /// 内部のノードが持つのは 1 始まりの <i>level</i>（1 = 最下位＝葉側 … <see cref="VariableCount"/> = 最上位＝根側）。
    /// 変換は
    /// <c>level = VariableCount - item</c> / <c>item = VariableCount - level</c> の 1 対 1 対応で、
    /// item 0 が根側に来る（docs/OPEN-QUESTIONS.md B5）。この向きにすると、
    /// 0-枝を先に辿る深さ優先の列挙がそのまま<b>指示ベクトルの辞書順</b>になる
    /// （<see cref="ZddEnumerationOrder.Default"/>）。
    /// 変換は <see cref="LevelOf"/> / <see cref="ItemOf"/> の 2 つだけが行い、他の場所では計算しない。
    /// </para>
    /// <para>
    /// <b>変数の個数は固定</b>: 生成後に増やすことはできない（docs/OPEN-QUESTIONS.md B7）。
    /// 動的に増やすには一意化表の作り直しが要り、得られる利便性に見合わない。
    /// </para>
    /// <para>
    /// <b>正準形</b>: ノードは必ず一意化表を通して作られ、ゼロサプレス削減規則もそこで適用される。
    /// したがって「同じ族 ⇔ 同じノード ID」が常に成り立ち、<see cref="Zdd"/> の等値比較は
    /// ノード ID の比較だけで済む。
    /// </para>
    /// <para>
    /// <b>スレッド安全性</b>: <b>スレッドセーフではない</b>（docs/OPEN-QUESTIONS.md B6）。
    /// 1 つのマネージャを複数スレッドから同時に触ってはならない。読み取りだけであっても、
    /// <see cref="Zdd.NodeCount"/> のような走査を含む API はノード表を参照するため保証の対象外。
    /// </para>
    /// <para>
    /// <b>破棄</b>: <see cref="Dispose"/> はノード表・一意化表・演算キャッシュへの参照を手放し、GC が回収できるようにする。
    /// アンマネージドな資源は持たないので、破棄を忘れてもリークはしない
    /// （大きな配列の回収が GC 任せになるだけ）。破棄後は、<b>表を読む操作</b>
    /// （<see cref="Empty"/> / <see cref="Base"/> / <see cref="Singleton"/> / <see cref="NodeCount"/> /
    /// <see cref="GetStatistics"/> と、
    /// これに属する <see cref="Zdd"/> の <see cref="Zdd.NodeCount"/> / <see cref="Zdd.Support"/>）が
    /// <see cref="ObjectDisposedException"/> になる。表を読まないもの
    /// （<see cref="VariableCount"/> / <see cref="IsDisposed"/> と、<see cref="Zdd"/> の等値比較・
    /// <see cref="Zdd.IsEmpty"/> / <see cref="Zdd.IsBase"/>）は破棄後も使える。
    /// </para>
    /// </remarks>
    public sealed class ZddManager : IDisposable
    {
        /// <summary>作業領域の貸出枠の初期段数。入れ子は「積 → 和」の 1 段だけなので、これで足りる。</summary>
        private const int InitialWorkspaceDepth = 2;

        private readonly int _variableCount;

        /// <summary>破棄されると <see langword="null"/> になる。破棄済みかどうかの判定も兼ねる。</summary>
        private UniqueTable? _table;

        /// <summary>演算結果のメモ表。<see cref="_table"/> と同時に手放す。</summary>
        private OperationCache? _cache;

        /// <summary>
        /// 冪集合 <c>2^U</c> の根ノード ID。<see cref="NodeTable.Bottom"/> なら未計算。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 変数の個数は固定で、一意化表は同じ族に必ず同じ ID を返すので、この値はマネージャの一生を
        /// 通じて変わらない。<see cref="NodeTable.Bottom"/> を「未計算」の番兵に使えるのは、
        /// <c>2^U</c> が少なくとも ∅ を含む＝空の族には決してならないからである。
        /// </para>
        /// <para>
        /// <b>ノード ID の意味が変わる操作をしたら捨てること</b>。将来の M5-3（ノード GC）が
        /// ノード表を組み替えるときは、<see cref="OperationCache.Clear"/> と同じくここも戻す。
        /// </para>
        /// </remarks>
        private int _powerSetRoot;

        /// <summary>
        /// 反復実装が使う作業領域の貸出枠。演算のたびに作り直さず、ここに置いて使い回す。
        /// 添字は<b>入れ子の深さ</b>で、深さ 0 が普通の演算、深さ 1 以上は
        /// 「演算の合成の途中で別の演算を呼んだ」ぶん（積 → 和、商 → 交わり）。
        /// 深さごとに別の作業領域を渡すので、同じものを 2 箇所で使うことはない。
        /// </summary>
        /// <remarks>
        /// 枠は使い終わっても手放さない。育った配列をそのまま次の演算に引き継ぐためで、
        /// これが無いと入れ子で呼ばれる演算が呼び出しのたびに作業領域を作り直すことになる
        /// （積は合成のたびに和を呼ぶので、ノード 1 個あたりのアロケーションになってしまう）。
        /// </remarks>
        private OperationWorkspace?[] _workspaces;

        /// <summary>いま貸し出している作業領域の個数（＝次に貸す枠の添字）。</summary>
        private int _workspaceDepth;

        /// <summary>変数の個数を指定してマネージャを作る。</summary>
        /// <param name="variableCount">
        /// 扱う変数（item）の個数。有効な item index は 0 … <paramref name="variableCount"/> - 1。
        /// 0 も許される（このとき <see cref="Empty"/> と <see cref="Base"/> しか作れない）。
        /// </param>
        /// <param name="options">
        /// 初期容量などの調整項目。<see langword="null"/> なら既定値を使う。
        /// 値はここで読み取られ、以後 <paramref name="options"/> を書き換えてもこのマネージャには影響しない。
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableCount"/> が負の場合。</exception>
        public ZddManager(int variableCount, ZddManagerOptions? options = null)
        {
            ThrowHelper.ThrowIfNegative(variableCount, nameof(variableCount));

            ZddManagerOptions effective = options ?? new ZddManagerOptions();

            _variableCount = variableCount;
            _table = new UniqueTable(
                new NodeTable(NodeTable.FirstNodeId + effective.InitialNodeCapacity),
                effective.InitialUniqueTableCapacity);
            _cache = new OperationCache(effective.InitialCacheCapacity, effective.MaxCacheCapacity);
            _workspaces = new OperationWorkspace?[InitialWorkspaceDepth];
        }

        /// <summary>このマネージャが扱う変数（item）の個数。生成後は変わらない。</summary>
        public int VariableCount => _variableCount;

        /// <summary>
        /// このマネージャが確保している非終端ノードの総数。族ごとの数ではなく、
        /// これまでに作られたすべての族が共有しているノードの合計。
        /// </summary>
        /// <exception cref="ObjectDisposedException">このマネージャが破棄済みの場合。</exception>
        public long NodeCount => Table.Count;

        /// <summary>このマネージャが <see cref="Dispose"/> 済みかどうか。</summary>
        public bool IsDisposed => _table is null;

        /// <summary>空の族 ∅（要素を 1 つも持たない族）。終端 ⊥ に対応する。</summary>
        /// <exception cref="ObjectDisposedException">このマネージャが破棄済みの場合。</exception>
        public Zdd Empty
        {
            get
            {
                EnsureNotDisposed();
                return new Zdd(this, NodeTable.Bottom);
            }
        }

        /// <summary>空集合だけを要素に持つ族 <c>{∅}</c>。終端 ⊤ に対応する。</summary>
        /// <exception cref="ObjectDisposedException">このマネージャが破棄済みの場合。</exception>
        public Zdd Base
        {
            get
            {
                EnsureNotDisposed();
                return new Zdd(this, NodeTable.Top);
            }
        }

        /// <summary>
        /// 1 要素集合だけを持つ族 <c>{{item}}</c> を返す。
        /// </summary>
        /// <param name="item">0 以上 <see cref="VariableCount"/> 未満の item index。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> が範囲外の場合。</exception>
        /// <exception cref="ObjectDisposedException">このマネージャが破棄済みの場合。</exception>
        public Zdd Singleton(int item)
        {
            UniqueTable table = Table;
            int level = LevelOf(item);

            // item を含まない集合は 1 つも無いので 0-枝は ⊥、item を除いた残りは空集合なので 1-枝は ⊤。
            return new Zdd(this, table.GetNode(level, NodeTable.Bottom, NodeTable.Top));
        }

        /// <summary>
        /// 内部の表がいまどうなっているかを 1 つの値にまとめて返す（docs/PLAN.md §4.6）。
        /// </summary>
        /// <returns>
        /// 呼び出した時点の写し。以後マネージャが変わっても、返した値は変わらない。
        /// </returns>
        /// <remarks>
        /// <para>
        /// 表を読むだけで、族の走査は行わない（<see cref="Zdd.NodeCount"/> と違って定数時間）。
        /// 統計をどう読むかは <see cref="ZddStatistics"/> の解説にまとめてある。
        /// </para>
        /// <para>
        /// 演算キャッシュのカウンタはマネージャを作ってからの積算値なので、区間で見たいときは
        /// 前後 2 回呼んで差を取る。
        /// </para>
        /// </remarks>
        /// <exception cref="ObjectDisposedException">このマネージャが破棄済みの場合。</exception>
        public ZddStatistics GetStatistics()
        {
            UniqueTable table = Table;
            OperationCache cache = Cache;
            NodeTable nodes = table.Nodes;

            return new ZddStatistics(
                nodeCount: nodes.Count,
                peakNodeCount: nodes.PeakCount,
                nodeTableCapacity: nodes.Capacity,
                uniqueTableCapacity: table.Capacity,
                uniqueTableCollisions: table.Collisions,
                cacheCapacity: cache.Capacity,
                maxCacheCapacity: cache.MaxCapacity,
                cacheLookups: cache.Lookups,
                cacheHits: cache.Hits,
                cacheOverwrites: cache.Collisions);
        }

        /// <summary>
        /// ノード表・一意化表・演算キャッシュへの参照を手放す。以後このマネージャと、これに属する
        /// <see cref="Zdd"/> への操作は <see cref="ObjectDisposedException"/> になる。
        /// 2 回目以降の呼び出しは何もしない。
        /// </summary>
        public void Dispose()
        {
            _table = null;
            _cache = null;
            _powerSetRoot = NodeTable.Bottom;
            _workspaces = Array.Empty<OperationWorkspace?>();
            _workspaceDepth = 0;
        }

        /// <summary>和 <c>f ∪ g</c>。どちらか一方にでも属する集合を持つ族を返す。</summary>
        /// <param name="f">左の族。このマネージャに属していなければならない。</param>
        /// <param name="g">右の族。このマネージャに属していなければならない。</param>
        internal Zdd Union(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Union, f, g);

        /// <summary>積 <c>f ∩ g</c>。両方に属する集合だけを持つ族を返す。</summary>
        /// <param name="f">左の族。このマネージャに属していなければならない。</param>
        /// <param name="g">右の族。このマネージャに属していなければならない。</param>
        internal Zdd Intersect(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Intersect, f, g);

        /// <summary>差 <c>f ∖ g</c>。<paramref name="f"/> のうち <paramref name="g"/> に無い集合を返す。</summary>
        /// <param name="f">左の族。このマネージャに属していなければならない。</param>
        /// <param name="g">右の族。このマネージャに属していなければならない。</param>
        internal Zdd Difference(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Difference, f, g);

        /// <summary>対称差 <c>f △ g</c>。ちょうど一方にだけ属する集合を返す。</summary>
        /// <param name="f">左の族。このマネージャに属していなければならない。</param>
        /// <param name="g">右の族。このマネージャに属していなければならない。</param>
        internal Zdd SymmetricDifference(in Zdd f, in Zdd g) =>
            ApplyBinary(ZddOperation.SymmetricDifference, f, g);

        /// <summary>積 <c>f * g</c>。<c>{ a ∪ b : a ∈ f, b ∈ g }</c> を返す。</summary>
        /// <param name="f">左の族。このマネージャに属していなければならない。</param>
        /// <param name="g">右の族。このマネージャに属していなければならない。</param>
        internal Zdd Product(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Product, f, g);

        /// <summary>商 <c>f / g</c>。</summary>
        /// <param name="f">割られる族。このマネージャに属していなければならない。</param>
        /// <param name="g">割る族。このマネージャに属していなければならない。</param>
        internal Zdd Quotient(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Quotient, f, g);

        /// <summary>剰余 <c>f % g</c>。</summary>
        /// <param name="f">割られる族。このマネージャに属していなければならない。</param>
        /// <param name="g">割る族。このマネージャに属していなければならない。</param>
        internal Zdd Remainder(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Remainder, f, g);

        /// <summary>Meet <c>f ⊓ g</c>。<c>{ a ∩ b : a ∈ f, b ∈ g }</c> を返す。</summary>
        /// <param name="f">左の族。このマネージャに属していなければならない。</param>
        /// <param name="g">右の族。このマネージャに属していなければならない。</param>
        internal Zdd Meet(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.Meet, f, g);

        /// <summary><paramref name="g"/> のいずれかを含む <paramref name="f"/> の要素だけを残す。</summary>
        /// <param name="f">ふるいにかけられる族。このマネージャに属していなければならない。</param>
        /// <param name="g">条件を与える族。このマネージャに属していなければならない。</param>
        internal Zdd SupersetsOf(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.SupersetsOf, f, g);

        /// <summary><paramref name="g"/> のいずれかに含まれる <paramref name="f"/> の要素だけを残す。</summary>
        /// <param name="f">ふるいにかけられる族。このマネージャに属していなければならない。</param>
        /// <param name="g">条件を与える族。このマネージャに属していなければならない。</param>
        internal Zdd SubsetsOf(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.SubsetsOf, f, g);

        /// <summary><paramref name="g"/> のどれの部分集合でもない <paramref name="f"/> の要素だけを残す。</summary>
        /// <param name="f">ふるいにかけられる族。このマネージャに属していなければならない。</param>
        /// <param name="g">条件を与える族。このマネージャに属していなければならない。</param>
        internal Zdd NonSubsetsOf(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.NonSubsetsOf, f, g);

        /// <summary><paramref name="g"/> のどれの上位集合でもない <paramref name="f"/> の要素だけを残す。</summary>
        /// <param name="f">ふるいにかけられる族。このマネージャに属していなければならない。</param>
        /// <param name="g">条件を与える族。このマネージャに属していなければならない。</param>
        internal Zdd NonSupersetsOf(in Zdd f, in Zdd g) => ApplyBinary(ZddOperation.NonSupersetsOf, f, g);

        /// <summary>
        /// 各集合の <paramref name="item"/> の有無を反転した族を返す。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="item">0 以上 <see cref="VariableCount"/> 未満の item index。</param>
        internal Zdd Change(in Zdd f, int item) => ApplyUnary(ZddOperation.Change, f, item, nameof(f));

        /// <summary>
        /// <paramref name="item"/> を含む集合だけを取り出し、そこから <paramref name="item"/> を除いた族を返す。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="item">0 以上 <see cref="VariableCount"/> 未満の item index。</param>
        internal Zdd OnSet(in Zdd f, int item) => ApplyUnary(ZddOperation.OnSet, f, item, nameof(f));

        /// <summary>
        /// <paramref name="item"/> を含まない集合だけを残した族を返す。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="item">0 以上 <see cref="VariableCount"/> 未満の item index。</param>
        internal Zdd OffSet(in Zdd f, int item) => ApplyUnary(ZddOperation.OffSet, f, item, nameof(f));

        /// <summary>
        /// <paramref name="items"/> の有無をまとめて反転した族を返す（<see cref="Change"/> の一般化）。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="items">反転する item index の並び。空なら <paramref name="f"/> がそのまま返る。</param>
        /// <remarks>
        /// <see cref="Change"/> を順に掛けるだけ。<see cref="Change"/> はどの item についても対合
        /// （2 回で元に戻る）で、item どうしの順序も結果に影響しないので、
        /// 同じ item を 2 度渡すと反転が打ち消し合う。
        /// 範囲検査は 1 つでも外れていれば<b>何も計算する前に</b>済ませる。
        /// </remarks>
        internal Zdd Flip(in Zdd f, ReadOnlySpan<int> items)
        {
            EnsureOwns(f, nameof(f));

            // 途中まで反転してから例外にすると、呼び出し側から見て何が起きたのか分からない。
            // 欲しいのはレベルではなく範囲検査そのものなので、結果は捨てる。
            foreach (int item in items)
            {
                _ = LevelOf(item);
            }

            // 破棄済みならここで ObjectDisposedException になる（表もキャッシュも触るため）。
            TuneCache();

            int result = f.Id;

            foreach (int item in items)
            {
                result = UnaryOperations.Apply(this, ZddOperation.Change, result, item);
            }

            return new Zdd(this, result);
        }

        /// <summary>包含関係で極大な要素だけを残した族を返す。</summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        internal Zdd Maximal(in Zdd f) => ApplyExtremal(ZddOperation.Maximal, f);

        /// <summary>包含関係で極小な要素だけを残した族を返す。</summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        internal Zdd Minimal(in Zdd f) => ApplyExtremal(ZddOperation.Minimal, f);

        /// <summary><paramref name="f"/> のどの要素とも交わる集合をすべて集めた族を返す。</summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        internal Zdd HittingSets(in Zdd f) => ApplyExtremal(ZddOperation.HittingSets, f);

        /// <summary>補 <c>2^U ∖ f</c>（<c>U</c> はこのマネージャの全変数）を返す。</summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        internal Zdd Complement(in Zdd f) => ApplyExtremal(ZddOperation.Complement, f);

        /// <summary>
        /// <paramref name="items"/> が表す集合が <paramref name="f"/> に属するかどうかを返す。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="items">調べる集合の item index。順不同・重複可。</param>
        /// <remarks>
        /// 族を作らないので、演算キャッシュを整える必要も作業領域を借りる必要も無い。
        /// </remarks>
        internal bool Contains(in Zdd f, ReadOnlySpan<int> items)
        {
            EnsureOwns(f, nameof(f));

            return QueryOperations.Contains(this, f.Id, items);
        }

        /// <summary>
        /// <paramref name="f"/> の <paramref name="index"/> 番目の集合を返す（unranking）。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="index">取り出す集合の順位。0 以上、族の濃度未満。</param>
        /// <param name="order">順位の数え方（列挙の順序と同じ）。</param>
        /// <remarks>
        /// 族を作らないので、演算キャッシュを整える必要も作業領域を借りる必要も無い。
        /// </remarks>
        internal int[] ElementAt(in Zdd f, BigInteger index, ZddEnumerationOrder order)
        {
            EnsureOwns(f, nameof(f));

            return SetRanking.ElementAt(this, f.Id, index, order);
        }

        /// <summary>
        /// <paramref name="items"/> が表す集合の <paramref name="f"/> における順位を返す（ranking）。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="items">調べる集合の item index。順不同・重複可。</param>
        /// <param name="order">順位の数え方（列挙の順序と同じ）。</param>
        internal BigInteger IndexOf(in Zdd f, ReadOnlySpan<int> items, ZddEnumerationOrder order)
        {
            EnsureOwns(f, nameof(f));

            return SetRanking.IndexOf(this, f.Id, items, order);
        }

        /// <summary><paramref name="f"/> から集合を 1 つ、一様ランダムに選んで返す。</summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="random">乱数の供給元。</param>
        internal int[] Sample(in Zdd f, Random random)
        {
            EnsureOwns(f, nameof(f));

            return SetRanking.Sample(this, f.Id, random);
        }

        /// <summary>
        /// <paramref name="f"/> から集合を <paramref name="count"/> 個、一様ランダムに選んで返す。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="count">取り出す個数。0 以上。</param>
        /// <param name="random">乱数の供給元。</param>
        internal int[][] Sample(in Zdd f, int count, Random random)
        {
            EnsureOwns(f, nameof(f));

            return SetRanking.Sample(this, f.Id, count, random);
        }

        /// <summary><paramref name="f"/> の中で重みが最大の集合を、その重みとともに返す。</summary>
        /// <typeparam name="TWeight">重みの型。</typeparam>
        /// <typeparam name="TOps">重みの演算。<c>struct</c> でなければならない。</typeparam>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="weights">item ごとの重み。長さは <see cref="VariableCount"/> と等しいこと。</param>
        /// <remarks>
        /// 族を作らないので、演算キャッシュを整える必要も作業領域を借りる必要も無い。
        /// </remarks>
        internal WeightedSet<TWeight> MaxWeight<TWeight, TOps>(in Zdd f, ReadOnlySpan<TWeight> weights)
            where TOps : struct, IWeightOps<TWeight>
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.Optimize<TWeight, TOps>(this, f.Id, weights, maximize: true);
        }

        /// <summary><paramref name="f"/> の中で重みが最小の集合を、その重みとともに返す。</summary>
        /// <typeparam name="TWeight">重みの型。</typeparam>
        /// <typeparam name="TOps">重みの演算。<c>struct</c> でなければならない。</typeparam>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="weights">item ごとの重み。長さは <see cref="VariableCount"/> と等しいこと。</param>
        internal WeightedSet<TWeight> MinWeight<TWeight, TOps>(in Zdd f, ReadOnlySpan<TWeight> weights)
            where TOps : struct, IWeightOps<TWeight>
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.Optimize<TWeight, TOps>(this, f.Id, weights, maximize: false);
        }

        /// <summary><paramref name="f"/> の中で重みが大きい順に <paramref name="k"/> 個の集合を返す。</summary>
        /// <typeparam name="TWeight">重みの型。</typeparam>
        /// <typeparam name="TOps">重みの演算。<c>struct</c> でなければならない。</typeparam>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="weights">item ごとの重み。長さは <see cref="VariableCount"/> と等しいこと。</param>
        /// <param name="k">取り出す個数。0 以上。</param>
        internal WeightedSet<TWeight>[] TopK<TWeight, TOps>(in Zdd f, ReadOnlySpan<TWeight> weights, int k)
            where TOps : struct, IWeightOps<TWeight>
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.TopK<TWeight, TOps>(this, f.Id, weights, k);
        }

        /// <summary>
        /// 各 item が独立に確率 <paramref name="probabilities"/> で選ばれるとき、
        /// 出来上がる集合が <paramref name="f"/> に属する確率。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="probabilities">
        /// item ごとの確率。長さは <see cref="VariableCount"/> と等しく、各値は 0 以上 1 以下。
        /// </param>
        internal double Probability(in Zdd f, ReadOnlySpan<double> probabilities)
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.Probability(this, f.Id, probabilities);
        }

        /// <summary>
        /// <paramref name="f"/> から集合を 1 つ一様に選んだときの、その集合の重みの期待値。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        /// <param name="weights">item ごとの重み。長さは <see cref="VariableCount"/> と等しいこと。</param>
        internal double ExpectedValue(in Zdd f, ReadOnlySpan<double> weights)
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.ExpectedValue(this, f.Id, weights);
        }

        /// <summary>
        /// <paramref name="f"/> から集合を 1 つ一様に選んだとき、item ごとにそれが含まれる確率。
        /// </summary>
        /// <param name="f">対象の族。このマネージャに属していなければならない。</param>
        internal double[] ItemFrequency(in Zdd f)
        {
            EnsureOwns(f, nameof(f));

            return WeightOperations.ItemFrequency(this, f.Id);
        }

        /// <summary><paramref name="f"/> の集合がすべて <paramref name="g"/> にも属するかどうか。</summary>
        /// <param name="f">左の族。このマネージャに属していなければならない。</param>
        /// <param name="g">右の族。このマネージャに属していなければならない。</param>
        internal bool IsSubsetOf(in Zdd f, in Zdd g)
        {
            EnsureOwns(f, nameof(f));
            EnsureOwns(g, nameof(g));

            return QueryOperations.IsSubsetOf(this, f.Id, g.Id);
        }

        /// <summary><paramref name="f"/> と <paramref name="g"/> に共通の集合があるかどうか。</summary>
        /// <param name="f">左の族。このマネージャに属していなければならない。</param>
        /// <param name="g">右の族。このマネージャに属していなければならない。</param>
        internal bool Overlaps(in Zdd f, in Zdd g)
        {
            EnsureOwns(f, nameof(f));
            EnsureOwns(g, nameof(g));

            return QueryOperations.Overlaps(this, f.Id, g.Id);
        }

        /// <summary>
        /// 全体集合の冪集合 <c>2^U</c>（<see cref="VariableCount"/> 個の item の全部分集合）の根ノード ID。
        /// </summary>
        /// <remarks>
        /// どの item も「入れても入れなくてもよい」ので、各レベルで 0-枝と 1-枝が同じ族を指す。
        /// ノードは変数の個数ぶんだけで、族としての大きさ（2^n 個の集合）とは無関係に小さい。
        /// <see cref="ZddOperation.Quotient"/>（<c>f / ∅</c>）と <see cref="ZddOperation.Complement"/> が
        /// 同じ全体集合を指すように、組み立てはここ 1 箇所に置く。
        /// <b>1 度組み立てたら覚えておく</b>（<see cref="_powerSetRoot"/>）。既存ノードなら一意化表を
        /// 引くだけとはいえ、変数 10 万のマネージャでは補を 1 回取るたびに 10 万回引くことになる。
        /// </remarks>
        /// <exception cref="ObjectDisposedException">このマネージャが破棄済みの場合。</exception>
        internal int PowerSetRoot()
        {
            // 破棄済みならここで例外になる。覚えた値を返すときも、この検査は先に通す。
            UniqueTable table = Table;

            if (_powerSetRoot != NodeTable.Bottom)
            {
                return _powerSetRoot;
            }

            int result = NodeTable.Top;

            for (int level = 1; level <= _variableCount; level++)
            {
                result = table.GetNode(level, result, result);
            }

            _powerSetRoot = result;
            return result;
        }

        /// <summary>
        /// item を取らない単項演算の共通の入口。所有マネージャの一致を確かめ、キャッシュを整えてから
        /// <see cref="ExtremalOperations.Apply"/> に渡す。
        /// </summary>
        private Zdd ApplyExtremal(ZddOperation op, in Zdd f)
        {
            EnsureOwns(f, nameof(f));

            // 破棄済みならここで ObjectDisposedException になる（表もキャッシュも触るため）。
            TuneCache();

            return new Zdd(this, ExtremalOperations.Apply(this, op, f.Id));
        }

        /// <summary>
        /// 単項演算の共通の入口。所有マネージャの一致を確かめ、キャッシュを整えてから
        /// <see cref="UnaryOperations.Apply"/> に渡す。<paramref name="item"/> の範囲検査は
        /// その中の <see cref="LevelOf"/> が行う。
        /// </summary>
        private Zdd ApplyUnary(ZddOperation op, in Zdd f, int item, string paramName)
        {
            EnsureOwns(f, paramName);

            // 破棄済みならここで ObjectDisposedException になる（表もキャッシュも触るため）。
            TuneCache();

            return new Zdd(this, UnaryOperations.Apply(this, op, f.Id, item));
        }

        /// <summary>
        /// 二項演算の共通の入口。両オペランドがこのマネージャのものであることを確かめ、
        /// キャッシュを整えてから演算の実装に渡す。
        /// </summary>
        /// <remarks>
        /// 集合演算（<see cref="BinaryOperations"/>）・家族代数の積・商・剰余
        /// （<see cref="FamilyAlgebraOperations"/>）・包含系のふるい
        /// （<see cref="ContainmentOperations"/>）は走査の形が違うので実装が別になっているが、
        /// 引数の検査とキャッシュの手入れは同じなので、入口はここ 1 つにまとめてある。
        /// </remarks>
        private Zdd ApplyBinary(ZddOperation op, in Zdd f, in Zdd g)
        {
            EnsureOwns(f, nameof(f));
            EnsureOwns(g, nameof(g));

            // 破棄済みならここで ObjectDisposedException になる（表もキャッシュも触るため）。
            TuneCache();

            int result = op switch
            {
                ZddOperation.Product or ZddOperation.Quotient or ZddOperation.Remainder =>
                    FamilyAlgebraOperations.Apply(this, op, f.Id, g.Id),
                ZddOperation.Meet
                    or ZddOperation.SupersetsOf
                    or ZddOperation.SubsetsOf
                    or ZddOperation.NonSubsetsOf
                    or ZddOperation.NonSupersetsOf =>
                    ContainmentOperations.Apply(this, op, f.Id, g.Id),
                _ => BinaryOperations.Apply(this, op, f.Id, g.Id),
            };

            return new Zdd(this, result);
        }

        /// <summary>
        /// 反復実装の作業領域を借りる。使い終わったら必ず <see cref="ReturnWorkspace"/> で返す。
        /// </summary>
        /// <remarks>
        /// 貸出中にもう一度借りると<b>別の</b>作業領域が返る（演算の中から別の演算を呼ぶ形になっても、
        /// 同じ作業領域を 2 箇所で使うことはない）。深さごとの枠は返しても手放さないので、
        /// 入れ子の内側で使う分も含めて、育った配列がそのまま次の演算に引き継がれる。
        /// </remarks>
        /// <exception cref="ObjectDisposedException">このマネージャが破棄済みの場合。</exception>
        internal OperationWorkspace RentWorkspace()
        {
            // 破棄後に貸出枠を作り直さないための番。演算の入口が先に弾くので通常は到達しない。
            EnsureNotDisposed();

            if (_workspaceDepth == _workspaces.Length)
            {
                Array.Resize(ref _workspaces, _workspaces.Length * 2);
            }

            OperationWorkspace workspace = _workspaces[_workspaceDepth] ??= new OperationWorkspace();
            _workspaceDepth++;
            return workspace;
        }

        /// <summary>
        /// 借りた作業領域を返す。中身は次の演算のために空にされ、枠はそのまま残る。
        /// </summary>
        /// <remarks>
        /// 貸し借りは入れ子（LIFO）でしか起きない。いちばん内側のものでなければ深さを戻さないので、
        /// 順序が狂っても枠が飛び越して空くことはない。
        /// </remarks>
        internal void ReturnWorkspace(OperationWorkspace workspace)
        {
            workspace.Reset();

            if (_workspaceDepth > 0 && ReferenceEquals(_workspaces[_workspaceDepth - 1], workspace))
            {
                _workspaceDepth--;
            }
        }

        /// <summary>
        /// このマネージャが使っている一意化表。破棄後は <see cref="ObjectDisposedException"/>。
        /// </summary>
        internal UniqueTable Table
        {
            get
            {
                UniqueTable? table = _table;
                if (table is null)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(ZddManager));
                }

                return table!;
            }
        }

        /// <summary>
        /// このマネージャが使っている演算キャッシュ。破棄後は <see cref="ObjectDisposedException"/>。
        /// </summary>
        internal OperationCache Cache
        {
            get
            {
                OperationCache? cache = _cache;
                if (cache is null)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(ZddManager));
                }

                return cache!;
            }
        }

        /// <summary>
        /// 演算キャッシュを現在のノード数に見合うサイズへ広げる。演算の入口（M1-5 以降）で呼ぶ。
        /// </summary>
        /// <remarks>
        /// 呼び忘れてもキャッシュは初期サイズのまま正しく働く（ヒット率が落ちるだけ）。
        /// </remarks>
        internal void TuneCache() => Cache.Tune(Table.Count);

        /// <summary>
        /// item index を内部の変数レベルに変換する。<c>level = VariableCount - item</c>。
        /// </summary>
        /// <param name="item">0 以上 <see cref="VariableCount"/> 未満の item index。</param>
        /// <returns>1 以上 <see cref="VariableCount"/> 以下のレベル。</returns>
        internal int LevelOf(int item)
        {
            if ((uint)item >= (uint)_variableCount)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(item),
                    _variableCount == 0
                        ? $"This manager has no variables, so there is no valid item index; '{nameof(item)}' was {item}."
                        : $"'{nameof(item)}' must be in the range 0..{_variableCount - 1}, but was {item}.");
            }

            return _variableCount - item;
        }

        /// <summary>
        /// 内部の変数レベルを item index に変換する。<c>item = VariableCount - level</c>。
        /// </summary>
        /// <param name="level">1 以上 <see cref="VariableCount"/> 以下のレベル（終端のレベル 0 は不可）。</param>
        /// <returns>0 以上 <see cref="VariableCount"/> 未満の item index。</returns>
        internal int ItemOf(int level)
        {
            if (level < 1 || level > _variableCount)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(level),
                    _variableCount == 0
                        ? $"This manager has no variables, so there is no valid level; '{nameof(level)}' was {level}."
                        : $"'{nameof(level)}' must be in the range 1..{_variableCount}, but was {level}.");
            }

            return _variableCount - level;
        }

        /// <summary>
        /// <paramref name="item"/> を分岐変数とするノードを 1 個作る。
        /// ゼロサプレス削減規則と一意化は一意化表が適用するので、既存の族と同じ形になれば同じ ID が返る。
        /// </summary>
        /// <param name="item">分岐変数の item index。</param>
        /// <param name="lo"><paramref name="item"/> を含まない側の族。</param>
        /// <param name="hi"><paramref name="item"/> を含む側の族から <paramref name="item"/> を除いたもの。</param>
        /// <remarks>
        /// 族を手で組み立てるための内部入口。公開するかどうかは、演算が一通り揃う M1-5 以降に判断する
        /// （docs/ROADMAP.md「まだ公開 API から到達できないコード」の扱い）。
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="lo"/> / <paramref name="hi"/> が別のマネージャに属する、無効なハンドルである、
        /// または <paramref name="item"/> より根側の変数を分岐変数に持つ場合。
        /// </exception>
        internal Zdd CreateNode(int item, in Zdd lo, in Zdd hi)
        {
            UniqueTable table = Table;
            int level = LevelOf(item);

            EnsureOwns(lo, nameof(lo));
            EnsureOwns(hi, nameof(hi));
            EnsureBelow(level, lo.Id, nameof(lo));
            EnsureBelow(level, hi.Id, nameof(hi));

            return new Zdd(this, table.GetNode(level, lo.Id, hi.Id));
        }

        /// <summary>
        /// <paramref name="zdd"/> がこのマネージャに属することを確かめる。
        /// 異なるマネージャの族を混ぜた演算は、ノード ID の意味が食い違うため必ず例外にする。
        /// </summary>
        internal void EnsureOwns(in Zdd zdd, string paramName)
        {
            if (ReferenceEquals(zdd.Owner, this))
            {
                return;
            }

            ThrowHelper.ThrowArgumentException(
                paramName,
                zdd.Owner is null
                    ? $"'{paramName}' is a default Zdd handle, which does not belong to any manager."
                    : $"'{paramName}' belongs to a different ZddManager; node ids are only meaningful within the manager that created them.");
        }

        /// <summary>
        /// <paramref name="rootId"/> から到達できる非終端ノードの個数を数える。
        /// </summary>
        internal long CountReachableNodes(int rootId)
        {
            if (NodeTable.IsTerminal(rootId))
            {
                return 0;
            }

            HashSet<int> visited = new HashSet<int>();
            Traverse(rootId, visited);
            return visited.Count;
        }

        /// <summary>
        /// <paramref name="rootId"/> から到達できるノードが実際に使っている item を昇順で返す。
        /// </summary>
        internal int[] CollectSupport(int rootId)
        {
            if (NodeTable.IsTerminal(rootId))
            {
                return Array.Empty<int>();
            }

            HashSet<int> visited = new HashSet<int>();
            Traverse(rootId, visited);

            NodeTable nodes = Table.Nodes;
            HashSet<int> levels = new HashSet<int>();
            foreach (int id in visited)
            {
                levels.Add(nodes[id].Level);
            }

            int[] items = new int[levels.Count];
            int next = 0;
            foreach (int level in levels)
            {
                items[next++] = ItemOf(level);
            }

            Array.Sort(items);
            return items;
        }

        /// <summary>
        /// <paramref name="rootId"/> から到達できる非終端ノードを <paramref name="visited"/> に集める。
        /// </summary>
        /// <remarks>
        /// <b>再帰しない</b>（docs/PLAN.md §4.5）。ZDD の深さは変数の個数そのもので、10 万規模になると
        /// 再帰では <c>StackOverflowException</c> でプロセスが即死する。.NET ではこれを catch できないため、
        /// 走査は必ず <c>int</c> 配列の明示スタックで書く。
        /// </remarks>
        private void Traverse(int rootId, HashSet<int> visited)
        {
            NodeTable nodes = Table.Nodes;

            int[] stack = new int[16];
            int top = 0;

            visited.Add(rootId);
            stack[top++] = rootId;

            while (top > 0)
            {
                ref ZddNode node = ref nodes[stack[--top]];
                int lo = node.Lo;
                int hi = node.Hi;

                if (!NodeTable.IsTerminal(lo) && visited.Add(lo))
                {
                    Push(ref stack, ref top, lo);
                }

                if (!NodeTable.IsTerminal(hi) && visited.Add(hi))
                {
                    Push(ref stack, ref top, hi);
                }
            }
        }

        private static void Push(ref int[] stack, ref int top, int id)
        {
            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = id;
        }

        /// <summary>
        /// 子ノードが親より真に下の水準にあることを確かめる。変数順序が守られていないノードを作ると、
        /// 正準形が壊れたまま後の演算まで気づけないため、生成の時点で弾く。
        /// </summary>
        private void EnsureBelow(int level, int childId, string paramName)
        {
            int childLevel = Table.Nodes[childId].Level;
            if (childLevel < level)
            {
                return;
            }

            ThrowHelper.ThrowArgumentException(
                paramName,
                $"'{paramName}' is rooted at item {ItemOf(childLevel)} (level {childLevel}), which is not below item {ItemOf(level)} (level {level}); " +
                "a node's children must branch on items that come later in the variable order.");
        }

        private void EnsureNotDisposed()
        {
            if (_table is null)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(ZddManager));
            }
        }
    }
}

using System;
using System.Collections.Generic;
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
    /// 0-枝を先に辿る深さ優先の列挙がそのまま item の辞書順になる。
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
    /// （<see cref="Empty"/> / <see cref="Base"/> / <see cref="Singleton"/> / <see cref="NodeCount"/> と、
    /// これに属する <see cref="Zdd"/> の <see cref="Zdd.NodeCount"/> / <see cref="Zdd.Support"/>）が
    /// <see cref="ObjectDisposedException"/> になる。表を読まないもの
    /// （<see cref="VariableCount"/> / <see cref="IsDisposed"/> と、<see cref="Zdd"/> の等値比較・
    /// <see cref="Zdd.IsEmpty"/> / <see cref="Zdd.IsBase"/>）は破棄後も使える。
    /// </para>
    /// </remarks>
    public sealed class ZddManager : IDisposable
    {
        private readonly int _variableCount;

        /// <summary>破棄されると <see langword="null"/> になる。破棄済みかどうかの判定も兼ねる。</summary>
        private UniqueTable? _table;

        /// <summary>演算結果のメモ表。<see cref="_table"/> と同時に手放す。</summary>
        private OperationCache? _cache;

        /// <summary>
        /// 反復実装が使う作業領域の貸出枠。演算のたびに作り直さず、ここに 1 個だけ置いて使い回す。
        /// <b>貸出中も参照は手放さない</b>（貸出中かどうかは <see cref="_workspaceInUse"/> が持つ）。
        /// 手放してしまうと、入れ子で借りた使い捨ての作業領域が枠に居座り、
        /// 大きく育った本来の作業領域のほうが捨てられてしまう。
        /// </summary>
        private OperationWorkspace? _workspace;

        /// <summary><see cref="_workspace"/> が貸出中かどうか。</summary>
        private bool _workspaceInUse;

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
        /// ノード表・一意化表・演算キャッシュへの参照を手放す。以後このマネージャと、これに属する
        /// <see cref="Zdd"/> への操作は <see cref="ObjectDisposedException"/> になる。
        /// 2 回目以降の呼び出しは何もしない。
        /// </summary>
        public void Dispose()
        {
            _table = null;
            _cache = null;
            _workspace = null;
            _workspaceInUse = false;
        }

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
        /// 反復実装の作業領域を借りる。使い終わったら必ず <see cref="ReturnWorkspace"/> で返す。
        /// </summary>
        /// <remarks>
        /// 枠は 1 個だけで、それが貸出中のときにもう一度借りると<b>使い捨ての</b>作業領域を作って返す
        /// （演算の中から別の演算を呼ぶ形になっても、同じ作業領域を 2 箇所で使うことはない）。
        /// 枠に置いてある作業領域は貸出中も差し替えないので、入れ子の内側で作られた使い捨ての分に
        /// 押し出されることはなく、育った配列がそのまま次の演算に引き継がれる。
        /// </remarks>
        /// <exception cref="ObjectDisposedException">このマネージャが破棄済みの場合。</exception>
        internal OperationWorkspace RentWorkspace()
        {
            // 破棄後に貸出枠を作り直さないための番。演算の入口が先に弾くので通常は到達しない。
            EnsureNotDisposed();

            if (_workspaceInUse)
            {
                return new OperationWorkspace();
            }

            OperationWorkspace workspace = _workspace ??= new OperationWorkspace();
            _workspaceInUse = true;
            return workspace;
        }

        /// <summary>
        /// 借りた作業領域を返す。中身は次の演算のために空にされる。
        /// 入れ子で借りた使い捨ての分は、空にしたうえで GC に任せる。
        /// </summary>
        internal void ReturnWorkspace(OperationWorkspace workspace)
        {
            workspace.Reset();

            if (ReferenceEquals(workspace, _workspace))
            {
                _workspaceInUse = false;
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

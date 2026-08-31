using System;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 族を作らずに真偽だけを答える問い合わせ（<see cref="Zdd.Contains(System.Collections.Generic.IEnumerable{int})"/> /
    /// <see cref="Zdd.IsSubsetOf"/> / <see cref="Zdd.Overlaps"/>）の実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>族を作らないのが要点</b>。<c>f.Overlaps(g)</c> は <c>(f &amp; g) != Empty</c> と同じ答を返すが、
    /// 交わりの ZDD を組み立てない。組み立てはノードの生成と一意化表への書き込みを伴うので、
    /// 「交わるかどうかだけ知りたい」場面ではそれ自体が無駄になる。
    /// <c>f.IsSubsetOf(g)</c> と <c>(f - g) == Empty</c> の関係も同じ。
    /// </para>
    /// <para>
    /// <b>短絡できる理由</b>: この 2 つは分解しても<b>合成の仕方が 1 種類しか出てこない</b>。
    /// <see cref="ZddOperation.Overlaps"/> は「部分問題のどれかが真なら真」（∨ だけの木）、
    /// <see cref="ZddOperation.IsSubsetOf"/> は「部分問題がすべて真なら真」（∧ だけの木）である。
    /// だから答は<b>到達できる終端条件の ∨／∧</b>そのもので、途中で決着する値
    /// （∨ なら真、∧ なら偽）が 1 つでも出た瞬間に、残りを見ずに返してよい。
    /// 合成の段を持たない単純な作業待ち行列で書けるのはこのためである。
    /// </para>
    /// <para>
    /// <b>同じ部分問題は 2 度見ない</b>。作業領域の途中結果表を「もう積んだ」印として使う。
    /// これが無いと、共有されたノードの対を親の数だけ辿り直すことになり、
    /// ノード数に対して指数的な手間になりうる。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>（docs/PLAN.md §4.5）。ZDD の深さは変数の個数そのもので、
    /// 10 万規模の族を素直な再帰で辿ると <c>StackOverflowException</c> になり、
    /// .NET ではこれを catch できずプロセスが即死する。
    /// </para>
    /// </remarks>
    internal static class QueryOperations
    {
        /// <summary>
        /// <paramref name="items"/> の集合が族に属するかどうかを調べる。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="items">調べる集合の item index。順不同で、同じ item が重なっていてもよい。</param>
        /// <remarks>
        /// <para>
        /// 根から終端まで<b>1 本の経路を降りるだけ</b>で、分岐も作業領域も要らない。
        /// ノードの item は根から葉へ向かって増えるので、集合の側を昇順に並べておけば
        /// 両者を同時に前進させられる。
        /// </para>
        /// <para>
        /// <b>飛ばされた item</b>に注意が要る。ノードが分岐に使っていない item は、
        /// そこから下の集合が<b>どれも含まない</b> item である（ゼロサプレス削減規則）。
        /// よって、いま見ているノードの item より小さい item が集合側に残っていたら、
        /// その時点で「属さない」と決まる。
        /// </para>
        /// <para>
        /// <b>計算量</b>: 経路の長さは変数の個数で抑えられるので O(変数の個数)。
        /// 並べ替えのぶん、集合の要素数を k として O(k log k) が加わる。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="items"/> に範囲外の item index が含まれる場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static bool Contains(ZddManager manager, int rootId, ReadOnlySpan<int> items)
        {
            EnsureItemsInRange(manager, items);

            // 昇順に並べておけば、根から葉へ向かって item が増える ZDD と同時に前進できる。
            // 空なら ToArray は割り当てずに空配列を返すので、そのまま渡してよい。
            int[] sorted = items.ToArray();
            Array.Sort(sorted);

            return ContainsSorted(manager, rootId, sorted);
        }

        /// <summary>
        /// 集合が<b>昇順に並んでいる</b>ことを前提にした <see cref="Contains"/>。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="sortedItems">
        /// 調べる集合の item index。<b>昇順</b>で、範囲検査は済んでいなければならない
        /// （<see cref="EnsureItemsInRange"/>）。同じ item が重なっていてもよい。
        /// </param>
        /// <remarks>
        /// 既に並べ替え済みの集合を持っている呼び出し側（<see cref="SetRanking"/> の順位づけ）が、
        /// 並べ直しと配列の作り直しを二重に払わずに済むようにするための入口。
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static bool ContainsSorted(ZddManager manager, int rootId, ReadOnlySpan<int> sortedItems)
        {
            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;

            if (sortedItems.Length == 0)
            {
                return HasEmptySet(nodes, rootId);
            }

            int next = 0;
            int id = rootId;

            while (!NodeTable.IsTerminal(id))
            {
                ref ZddNode node = ref nodes[id];
                int item = manager.ItemOf(node.Level);

                if (next < sortedItems.Length && sortedItems[next] < item)
                {
                    // この item を分岐に使うノードはもう現れない ＝ ここから下の集合はどれも含まない。
                    return false;
                }

                if (next < sortedItems.Length && sortedItems[next] == item)
                {
                    // 同じ item が重なって渡されていても、集合としては 1 つ。
                    do
                    {
                        next++;
                    }
                    while (next < sortedItems.Length && sortedItems[next] == item);

                    id = node.Hi;
                }
                else
                {
                    id = node.Lo;
                }
            }

            // 終端に着いても、まだ使い残した item があれば別の集合を辿ったことになる。
            return id == NodeTable.Top && next == sortedItems.Length;
        }

        /// <summary>
        /// item がすべてこのマネージャの宇宙に収まっていることを確かめる。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="items">調べる item index。</param>
        /// <remarks>
        /// 途中まで降りてから例外にすると、呼び出し側から見て何が起きたのか分からない
        /// （<see cref="ZddManager.Flip"/> と同じ手つき）。欲しいのは範囲検査そのものなので、
        /// <see cref="ZddManager.LevelOf"/> の結果は捨てる。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="items"/> に範囲外の item index が含まれる場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static void EnsureItemsInRange(ZddManager manager, ReadOnlySpan<int> items)
        {
            // 破棄済みならここで ObjectDisposedException になる（降り始める前に）。
            _ = manager.Table;

            foreach (int item in items)
            {
                _ = manager.LevelOf(item);
            }
        }

        /// <summary>
        /// <paramref name="fRoot"/> の族が <paramref name="gRoot"/> の族に<b>族として</b>含まれるか
        /// （<c>f</c> のどの集合も <c>g</c> に属するか）を調べる。
        /// </summary>
        /// <param name="manager">両方の族を所有するマネージャ。</param>
        /// <param name="fRoot">左の族の根ノード ID。</param>
        /// <param name="gRoot">右の族の根ノード ID。</param>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static bool IsSubsetOf(ZddManager manager, int fRoot, int gRoot) =>
            Search(manager, ZddOperation.IsSubsetOf, fRoot, gRoot);

        /// <summary>
        /// 2 つの族に共通の集合があるかどうかを調べる。
        /// </summary>
        /// <param name="manager">両方の族を所有するマネージャ。</param>
        /// <param name="fRoot">左の族の根ノード ID。</param>
        /// <param name="gRoot">右の族の根ノード ID。</param>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static bool Overlaps(ZddManager manager, int fRoot, int gRoot) =>
            Search(manager, ZddOperation.Overlaps, fRoot, gRoot);

        /// <summary>
        /// ノードの対を辿り、決着する終端条件が出たらそこで打ち切る。
        /// </summary>
        /// <remarks>
        /// <paramref name="op"/> が <see cref="ZddOperation.Overlaps"/> なら「真が 1 つ出れば真」、
        /// <see cref="ZddOperation.IsSubsetOf"/> なら「偽が 1 つ出れば偽」。
        /// どちらも「決着する値」を <c>decisive</c> として同じ走査で書ける。
        /// </remarks>
        private static bool Search(ZddManager manager, ZddOperation op, int fRoot, int gRoot)
        {
            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;

            // ∨ の木なら真で、∧ の木なら偽で決着する。決着しなければ、その否定が答になる。
            bool decisive = op == ZddOperation.Overlaps;

            // 終端が絡む組合せはここで片付く。作業領域を借りずに返せるので、
            // f.Overlaps(manager.Empty) のような呼び出しは表に触れない。
            if (TryResolve(nodes, op, fRoot, gRoot, out bool resolved))
            {
                return resolved;
            }

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                Remember(work, op, fRoot, gRoot);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    NodePair.Split(
                        nodes,
                        OperationKey.LeftOf(key),
                        OperationKey.RightOf(key),
                        out _,
                        out int f0,
                        out int f1,
                        out int g0,
                        out int g1);

                    if (!TryEnqueue(work, nodes, op, decisive, f0, g0) ||
                        !TryEnqueue(work, nodes, op, decisive, f1, g1))
                    {
                        return decisive;
                    }
                }

                return !decisive;
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>
        /// 部分問題を積む。<b>決着する値が出たら <see langword="false"/> を返す</b>（走査を打ち切る合図）。
        /// </summary>
        private static bool TryEnqueue(
            OperationWorkspace work,
            NodeTable nodes,
            ZddOperation op,
            bool decisive,
            int f,
            int g)
        {
            if (TryResolve(nodes, op, f, g, out bool resolved))
            {
                return resolved != decisive;
            }

            Remember(work, op, f, g);
            return true;
        }

        /// <summary>まだ見ていない部分問題なら積み、「もう積んだ」印を残す。</summary>
        /// <remarks>
        /// 途中結果表は本来「ノード ID → 結果ノード ID」を覚えるものだが、ここで欲しいのは
        /// 訪問済みかどうかだけなので、値は使わず 0 を入れておく。
        /// </remarks>
        private static void Remember(OperationWorkspace work, ZddOperation op, int f, int g)
        {
            long key = OperationKey.Of(op, f, g);

            if (work.HasResult(key))
            {
                return;
            }

            work.SetResult(key, 0);
            work.PushVisit(key);
        }

        /// <summary>
        /// 分解せずに答が決まる組合せかどうかを見る。決まるなら <see langword="true"/> を返し、
        /// <paramref name="resolved"/> にその答を入れる。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b><see cref="ZddOperation.IsSubsetOf"/></b>（<c>f ⊆ g</c>）:
        /// <c>f</c> が ∅ なら空虚に真、<c>f</c> と <c>g</c> が同じ族でも真。
        /// そうでなく <c>g</c> が ∅ なら、<c>f</c> は空でないので偽。
        /// <c>f</c> が <c>{∅}</c> なら <c>g</c> が空集合を持つかどうかで決まる。
        /// <c>g</c> が <c>{∅}</c> のときは、ここへ来る <c>f</c> は非終端で、
        /// 非終端ノードの 1-枝は決して ⊥ にならない（ゼロサプレス削減規則）＝
        /// <c>f</c> は空でない集合を必ず持つので偽。
        /// </para>
        /// <para>
        /// <b><see cref="ZddOperation.Overlaps"/></b>: どちらかが ∅ なら交わらない。
        /// 同じ族なら（∅ でない以上）自分自身と交わる。片方が <c>{∅}</c> なら、
        /// もう片方が空集合を持つかどうかで決まる。
        /// </para>
        /// <para>
        /// どちらの演算でも、ここを抜けた時点で <c>f</c> と <c>g</c> は<b>両方とも非終端</b>である。
        /// <see cref="NodePair.Split"/> が前提とする条件（終端どうしの対は来ない）はこれで満たされる。
        /// </para>
        /// </remarks>
        private static bool TryResolve(NodeTable nodes, ZddOperation op, int f, int g, out bool resolved)
        {
            if (op == ZddOperation.IsSubsetOf)
            {
                if (f == NodeTable.Bottom || f == g)
                {
                    resolved = true;
                    return true;
                }

                if (g == NodeTable.Bottom || g == NodeTable.Top)
                {
                    resolved = false;
                    return true;
                }

                if (f == NodeTable.Top)
                {
                    resolved = HasEmptySet(nodes, g);
                    return true;
                }

                resolved = false;
                return false;
            }

            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                resolved = false;
                return true;
            }

            if (f == g)
            {
                resolved = true;
                return true;
            }

            if (f == NodeTable.Top || g == NodeTable.Top)
            {
                resolved = HasEmptySet(nodes, f == NodeTable.Top ? g : f);
                return true;
            }

            resolved = false;
            return false;
        }

        /// <summary>族が空集合を要素に持つかどうか。0-枝だけを辿った先が ⊤ かどうかで決まる。</summary>
        /// <remarks>
        /// どのノードでも「item を含まない集合」は 0-枝の側にしかいないので、
        /// 空集合は 0-枝の連なりの先にしかありえない。長さは変数の個数で抑えられる。
        /// </remarks>
        private static bool HasEmptySet(NodeTable nodes, int id)
        {
            while (!NodeTable.IsTerminal(id))
            {
                id = nodes[id].Lo;
            }

            return id == NodeTable.Top;
        }
    }
}

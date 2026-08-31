using System;
using System.Collections.Generic;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 族に属する集合を 1 つずつ取り出す遅延列挙の実装。<see cref="Zdd.GetEnumerator"/> /
    /// <see cref="Zdd.Sets(ZddEnumerationOrder)"/> の中身。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>数える（<see cref="Zdd.Count"/>）のとは計算量が違う</b>。数え上げはノード数に比例するが、
    /// 列挙は<b>出す集合の個数</b>に比例する。10^24 個の解を数えるのは一瞬でも、全部取り出すことは
    /// できない。だからこそ<b>遅延</b>で返す: <c>Take(10)</c> や最初の 1 個で打ち切る使い方が
    /// 族の大きさに関係なく即座に終わる。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>。ZDD の深さは変数の個数そのもので、10 万規模の族を素直な再帰で辿ると
    /// <c>StackOverflowException</c> になり、.NET ではこれを catch できずプロセスが即死する
    /// （docs/PLAN.md §4.5）。走査は <c>int</c> 配列の明示スタックで行う。
    /// </para>
    /// <para>
    /// <b>作業領域はマネージャから借りない</b>。<see cref="ZddManager.RentWorkspace"/> の貸し借りは
    /// 入れ子（LIFO）が前提だが、遅延列挙は <c>yield return</c> で呼び出し元に制御を返すため、
    /// その間に別の演算が走りうる。列挙器は自前のスタックを持つ。
    /// </para>
    /// <para>
    /// <b>返す配列は毎回新しい</b>（docs/ROADMAP.md M1-13）。経路そのものは 1 本の作業配列で持ち回り、
    /// 終端 ⊤ に着くたびに<b>その時点の中身を写した</b> <c>int[]</c> を返す。使い回すと
    /// <c>ToList()</c> した全要素が同じ配列になるという静かな罠が生まれるので、既定は安全側に倒す。
    /// バッファを使い回す高速版が要るなら、別 API として足す（<c>EnumerateInto(Span&lt;int&gt;)</c> など）。
    /// </para>
    /// </remarks>
    internal static class SetEnumeration
    {
        /// <summary>明示スタックの初期段数。足りなくなれば倍化する。</summary>
        private const int InitialStackCapacity = 32;

        /// <summary>経路と 0-枝の連なりを溜める作業配列の初期の大きさ。</summary>
        private const int InitialPathCapacity = 16;

        /// <summary>「経路の末尾の item を外す」印。ノード ID は非負なので取り違えない。</summary>
        private const int PopItem = -1;

        /// <summary>
        /// 族に属する集合を <paramref name="order"/> の順に返す遅延列挙を作る。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <param name="order">集合を返す順序。</param>
        /// <remarks>
        /// 引数の検査だけはここで<b>先に</b>済ませる。<c>yield return</c> を含むメソッドは本体が
        /// 最初の <c>MoveNext</c> まで動かないので、そこに検査を置くと
        /// 「<c>Sets()</c> を呼んだ場所ではなく <c>foreach</c> の場所で例外が出る」ことになる。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="order"/> が定義されていない値の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static IEnumerable<int[]> Enumerate(ZddManager manager, int rootId, ZddEnumerationOrder order)
        {
            EnsureDefinedOrder(order);

            // 破棄済みならここで ObjectDisposedException になる（列挙を始めてからではなく）。
            _ = manager.Table;

            return Traverse(manager, rootId, order == ZddEnumerationOrder.Lexicographic);
        }

        /// <summary>
        /// 順序が定義された値であることを確かめる。
        /// </summary>
        /// <param name="order">検査する順序。</param>
        /// <remarks>
        /// 順位づけ（<see cref="SetRanking"/>）も同じ順序を受け取るので、検査はここ 1 箇所に置く。
        /// 順序の意味を決めているのが列挙の側だからで、2 箇所に書くと片方だけ直されうる。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="order"/> が定義されていない値の場合。
        /// </exception>
        public static void EnsureDefinedOrder(ZddEnumerationOrder order)
        {
            if (order is not (ZddEnumerationOrder.Default or ZddEnumerationOrder.Lexicographic))
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(order),
                    $"'{nameof(order)}' must be a defined {nameof(ZddEnumerationOrder)} value, but was {(int)order}.");
            }
        }

        /// <summary>
        /// 根から終端 ⊤ までの経路を深さ優先で辿り、着くたびに経路上の item を返す。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>スタックに積むもの</b>は 3 種類あり、符号で見分ける:
        /// 非負ならノード ID（そこへ降りる）、<see cref="PopItem"/> なら経路の末尾を外す、
        /// それ以外の負値なら <c>-(item + 2)</c> で item を経路に足す。
        /// item を足す印と外す印は必ず対で積むので、経路は走査の入れ子とぴったり一致する。
        /// </para>
        /// <para>
        /// <b>0-枝優先（<see cref="ZddEnumerationOrder.Default"/>）</b>: ノード <c>v</c> では
        /// 「0-枝 → <c>v</c> を足す → 1-枝 → <c>v</c> を外す」の順に処理したいので、
        /// スタック（LIFO）へはその逆順に積む。
        /// </para>
        /// <para>
        /// <b>列としての辞書順（<see cref="ZddEnumerationOrder.Lexicographic"/>）</b>: 空列は
        /// どの列の接頭辞でもあるから最小で、それ以外は先頭要素が小さいほど先に来る。
        /// ノード <c>n</c> から 0-枝だけを辿った連なり（0-枝の連なりの先が終端 ⊤ なら、
        /// その部分族は空集合を含む）をまず見て、空集合があればそれを先に返し、
        /// 続けて連なり上のノードを<b>根側から順に</b>、その 1-枝へ降りる。
        /// 根側のノードほど item が小さい＝先頭要素が小さいので、これがそのまま列の辞書順になる。
        /// </para>
        /// </remarks>
        private static IEnumerable<int[]> Traverse(ZddManager manager, int rootId, bool lexicographic)
        {
            NodeTable nodes = manager.Table.Nodes;

            int[] stack = new int[InitialStackCapacity];
            int top = 0;

            // いま辿っている経路上の item（根側から順に入るので、常に昇順）。
            int[] path = new int[InitialPathCapacity];
            int pathLength = 0;

            // 列の辞書順でだけ使う、0-枝の連なりの控え。積み終われば用済みなので使い回してよい。
            int[] chain = lexicographic ? new int[InitialPathCapacity] : Array.Empty<int>();

            Push(ref stack, ref top, rootId);

            while (top > 0)
            {
                int entry = stack[--top];

                if (entry == PopItem)
                {
                    pathLength--;
                    continue;
                }

                if (entry < 0)
                {
                    Append(ref path, ref pathLength, -entry - 2);
                    continue;
                }

                // ⊥ に着いた経路は集合を 1 つも生まないので、何もしない。
                if (entry == NodeTable.Bottom)
                {
                    continue;
                }

                if (!lexicographic)
                {
                    if (entry == NodeTable.Top)
                    {
                        yield return path.AsSpan(0, pathLength).ToArray();
                        continue;
                    }

                    ZddNode node = nodes[entry];

                    Push(ref stack, ref top, PopItem);
                    Push(ref stack, ref top, node.Hi);
                    Push(ref stack, ref top, -(manager.ItemOf(node.Level) + 2));
                    Push(ref stack, ref top, node.Lo);
                    continue;
                }

                int chainLength = 0;
                int id = entry;
                while (!NodeTable.IsTerminal(id))
                {
                    Append(ref chain, ref chainLength, id);
                    id = nodes[id].Lo;
                }

                // 0-枝だけを辿った先が ⊤ ＝ この部分族は空集合を含む。列としては最小なので先に返す。
                if (id == NodeTable.Top)
                {
                    yield return path.AsSpan(0, pathLength).ToArray();
                }

                // 連なりの根側から順に 1-枝へ降りたいので、スタックへは末尾側から積む。
                for (int i = chainLength - 1; i >= 0; i--)
                {
                    ZddNode node = nodes[chain[i]];

                    Push(ref stack, ref top, PopItem);
                    Push(ref stack, ref top, node.Hi);
                    Push(ref stack, ref top, -(manager.ItemOf(node.Level) + 2));
                }
            }
        }

        private static void Push(ref int[] stack, ref int top, int entry)
        {
            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = entry;
        }

        private static void Append(ref int[] buffer, ref int length, int value)
        {
            if (length == buffer.Length)
            {
                // 使わない側は空配列で持っているので、倍化では伸びない。
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, InitialPathCapacity));
            }

            buffer[length++] = value;
        }
    }
}

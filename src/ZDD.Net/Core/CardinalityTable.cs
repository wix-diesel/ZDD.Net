using System;
using System.Collections.Generic;
using System.Numerics;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 族に属する集合の個数を<b>ノードごとに</b>覚えた表。<see cref="SetRanking"/> の
    /// unranking / ranking / サンプリングが降りる先の枝を選ぶために使う。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="Zdd.Count"/> との違いは「根の値だけか、全ノードぶんか」</b>。
    /// <see cref="ZddEvaluation.Evaluate{TEval, TValue}"/> は畳み込みの途中結果を評価の間だけ持ち、
    /// 返すのは根の値 1 つである。順位づけはそれでは足りない: <c>k</c> 番目の集合を取り出すには
    /// 「いま居るノードの 0-枝の先に集合がいくつあるか」を経路上のノードごとに問う必要があり、
    /// その問いに答えられるのは<b>部分濃度の表</b>だけである。だから走査の形（反復・メモ化・
    /// 漸化式 <c>lo + hi</c>）は <see cref="CardinalityEval"/> と同じでも、結果の持ち方が違う。
    /// </para>
    /// <para>
    /// <b>空集合を持つかどうかも一緒に覚える</b>（<see cref="HasEmptySet"/>）。
    /// 列としての辞書順（<see cref="ZddEnumerationOrder.Lexicographic"/>）では空集合が最小なので、
    /// 順位を数えるたびに「この部分族は空集合を含むか」を問うことになる。素直に 0-枝の連なりを
    /// 辿ると 1 回あたり O(変数の個数) かかり、経路上で毎回やれば O(変数の個数^2) になってしまう。
    /// 漸化式は <c>hasEmptySet(n) = hasEmptySet(n.Lo)</c> だけなので、濃度と同じ 1 回の走査で
    /// ついでに求まる。
    /// </para>
    /// <para>
    /// <b>表は作るたびに捨てる</b>（マネージャには覚えさせない）。族は不変なので覚えておけば
    /// 使い回せるが、覚え先はマネージャ側になり、ノード ID の意味が変わる操作（将来の
    /// ノード GC。docs/ROADMAP.md M5-3）のたびに捨てる約束が 1 つ増える。
    /// 一方で作る手間はノード数ぶんの足し算 1 回であり、<see cref="Zdd.Count"/> を呼ぶのと同じである。
    /// 何度も引きたい呼び出し（<see cref="Zdd.Sample(int, System.Random)"/>）は
    /// <b>1 本の表を作って n 回引く</b>ので、そこでは作り直しは起きない。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>（docs/PLAN.md §4.5）。ZDD の深さは変数の個数そのもので、
    /// 10 万規模の族を素直な再帰で辿ると <c>StackOverflowException</c> になり、
    /// .NET ではこれを catch できずプロセスが即死する。走査は <c>int</c> 配列の明示スタックで行う。
    /// </para>
    /// </remarks>
    internal sealed class CardinalityTable
    {
        /// <summary>明示スタックの初期段数。足りなくなれば倍化する。</summary>
        private const int InitialStackCapacity = 32;

        /// <summary>非終端ノード ID → その部分族の情報。終端は表に入れない（値が自明なため）。</summary>
        private readonly Dictionary<int, Entry> _entries;

        private CardinalityTable(Dictionary<int, Entry> entries) => _entries = entries;

        /// <summary>
        /// <paramref name="rootId"/> から到達できる全ノードの部分濃度を求めた表を作る。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <remarks>
        /// 走査はポストオーダー（子が片付いてから親を合成する）で、ノード 1 個につき足し算 1 回。
        /// 共有されたノードは何人の親から指されていても 1 度しか計算されないので、
        /// 手間は族の大きさ（集合の個数）ではなく<b>ノード数</b>に比例する。
        /// </remarks>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static CardinalityTable Build(ZddManager manager, int rootId)
        {
            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;

            Dictionary<int, Entry> entries = new Dictionary<int, Entry>();

            if (NodeTable.IsTerminal(rootId))
            {
                return new CardinalityTable(entries);
            }

            // スタックに積むのは 2 種類で、符号で見分ける: 非負なら「これから計算するノード」、
            // 負なら「子が片付いたので合成するノード」（ビット反転して積む）。
            // 非終端のノード ID は 2 以上なので、反転すれば必ず負になり取り違えない。
            int[] stack = new int[InitialStackCapacity];
            int top = 0;

            Push(ref stack, ref top, rootId);

            while (top > 0)
            {
                int item = stack[--top];

                if (item < 0)
                {
                    int id = ~item;
                    int lo;
                    int hi;
                    {
                        ref ZddNode node = ref nodes[id];
                        lo = node.Lo;
                        hi = node.Hi;
                    }

                    // 濃度は「item を含まない集合の個数」＋「item を含む集合の個数」。
                    // 空集合は 0-枝の側にしかいないので、その有無は 0-枝から受け継ぐだけでよい。
                    entries[id] = new Entry(
                        CountIn(entries, lo) + CountIn(entries, hi),
                        HasEmptySetIn(entries, lo));
                    continue;
                }

                // 別の親が既に片付けていれば、それ以上何もしない。
                if (entries.ContainsKey(item))
                {
                    continue;
                }

                int childLo;
                int childHi;
                {
                    ref ZddNode node = ref nodes[item];
                    childLo = node.Lo;
                    childHi = node.Hi;
                }

                // 自分を先に積み、その上に未計算の子を積む（LIFO なので子が先に片付く）。
                Push(ref stack, ref top, ~item);

                if (!NodeTable.IsTerminal(childLo) && !entries.ContainsKey(childLo))
                {
                    Push(ref stack, ref top, childLo);
                }

                if (!NodeTable.IsTerminal(childHi) && !entries.ContainsKey(childHi))
                {
                    Push(ref stack, ref top, childHi);
                }
            }

            return new CardinalityTable(entries);
        }

        /// <summary><paramref name="id"/> を根とする部分族に属する集合の個数。</summary>
        /// <param name="id">この表を作ったときの根から到達できるノード ID（終端でもよい）。</param>
        public BigInteger CountOf(int id) => CountIn(_entries, id);

        /// <summary><paramref name="id"/> を根とする部分族が空集合を要素に持つかどうか。</summary>
        /// <param name="id">この表を作ったときの根から到達できるノード ID（終端でもよい）。</param>
        public bool HasEmptySet(int id) => HasEmptySetIn(_entries, id);

        private static BigInteger CountIn(Dictionary<int, Entry> entries, int id)
        {
            if (NodeTable.IsTerminal(id))
            {
                // ⊤（{∅}）は集合を 1 つ持ち、⊥（∅）は 1 つも持たない。
                return id == NodeTable.Top ? BigInteger.One : BigInteger.Zero;
            }

            return EntryIn(entries, id).Count;
        }

        private static bool HasEmptySetIn(Dictionary<int, Entry> entries, int id)
        {
            if (NodeTable.IsTerminal(id))
            {
                return id == NodeTable.Top;
            }

            return EntryIn(entries, id).HasEmptySet;
        }

        private static Entry EntryIn(Dictionary<int, Entry> entries, int id)
        {
            if (!entries.TryGetValue(id, out Entry entry))
            {
                // ポストオーダーなので、子は親より先に片付いているはず。ここに来るのは走査か
                // 呼び出し側が壊れたときだけで、黙って 0 を返すと誤った順位が静かに返ってしまう。
                ThrowHelper.ThrowInvalidOperationException(
                    $"The cardinality of node {id} was read before it was computed.");
            }

            return entry;
        }

        private static void Push(ref int[] stack, ref int top, int item)
        {
            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = item;
        }

        /// <summary>1 つのノードについて覚えておくこと。</summary>
        private readonly struct Entry
        {
            public Entry(BigInteger count, bool hasEmptySet)
            {
                Count = count;
                HasEmptySet = hasEmptySet;
            }

            /// <summary>このノードを根とする部分族に属する集合の個数。</summary>
            public BigInteger Count { get; }

            /// <summary>このノードを根とする部分族が空集合を要素に持つかどうか。</summary>
            public bool HasEmptySet { get; }
        }
    }
}

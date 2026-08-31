using System;
using System.Collections.Generic;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 根から到達できる非終端ノードを<b>子が親より先に来る順</b>（ポストオーダー）に並べた表。
    /// ノードごとの DP 表を「配列 1 本 ＋ 添字」で持つための土台。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="ZddEvaluation"/> との違いは「根の値だけか、全ノードぶんか」</b>
    /// （<see cref="CardinalityTable"/> と同じ事情）。重み最適化は最適値のほかに<b>最適集合</b>を
    /// 復元する必要があり、そのためには経路上のノードごとの DP 値が要る。
    /// <see cref="Zdd.ItemFrequency"/> の DP に至っては、根から葉へ向かう<b>逆向き</b>の走査も要る。
    /// どちらも「並び 1 本」を先に作っておけば、あとは <c>for</c> ループになる。
    /// </para>
    /// <para>
    /// <b>並びの約束</b>: <see cref="Ids"/> の中で、子は必ず親より小さい添字に居る。したがって
    /// </para>
    /// <list type="bullet">
    /// <item><description>先頭から末尾へ回せば<b>ボトムアップ</b>（子の値が先に確定する）</description></item>
    /// <item><description>末尾から先頭へ回せば<b>トップダウン</b>（親からの寄せ集めが先に確定する）</description></item>
    /// </list>
    /// <para>
    /// になる。末尾は必ず根である。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>（docs/PLAN.md §4.5）。ZDD の深さは変数の個数そのもので、
    /// 10 万規模の族を素直な再帰で辿ると <c>StackOverflowException</c> になり、
    /// .NET ではこれを catch できずプロセスが即死する。走査は <c>int</c> 配列の明示スタックで行う。
    /// </para>
    /// </remarks>
    internal sealed class NodeOrder
    {
        /// <summary>明示スタックの初期段数。足りなくなれば倍化する。</summary>
        private const int InitialStackCapacity = 32;

        private readonly int[] _ids;
        private readonly Dictionary<int, int> _slots;

        private NodeOrder(int[] ids, Dictionary<int, int> slots)
        {
            _ids = ids;
            _slots = slots;
        }

        /// <summary>並びに入っている非終端ノードの個数。</summary>
        public int Count => _ids.Length;

        /// <summary>子が親より先に来る順に並べたノード ID。末尾が根。</summary>
        public ReadOnlySpan<int> Ids => _ids;

        /// <summary>
        /// <paramref name="rootId"/> から到達できる非終端ノードを並べる。終端だけなら空の並びになる。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="rootId">族の根ノード ID。</param>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static NodeOrder Build(ZddManager manager, int rootId)
        {
            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;

            List<int> ids = new List<int>();
            Dictionary<int, int> slots = new Dictionary<int, int>();

            if (NodeTable.IsTerminal(rootId))
            {
                return new NodeOrder(Array.Empty<int>(), slots);
            }

            // スタックに積むのは 2 種類で、符号で見分ける: 非負なら「これから降りるノード」、
            // 負なら「子が片付いたので並びに加えるノード」（ビット反転して積む）。
            // 非終端のノード ID は 2 以上なので、反転すれば必ず負になり取り違えない。
            int[] stack = new int[InitialStackCapacity];
            int top = 0;

            Push(ref stack, ref top, rootId);

            while (top > 0)
            {
                int entry = stack[--top];

                if (entry < 0)
                {
                    int id = ~entry;
                    slots[id] = ids.Count;
                    ids.Add(id);
                    continue;
                }

                // 別の親が既に片付けていれば、それ以上何もしない。
                if (slots.ContainsKey(entry))
                {
                    continue;
                }

                int lo;
                int hi;
                {
                    ref ZddNode node = ref nodes[entry];
                    lo = node.Lo;
                    hi = node.Hi;
                }

                // 自分を先に積み、その上に未処理の子を積む（LIFO なので子が先に片付く）。
                Push(ref stack, ref top, ~entry);

                if (!NodeTable.IsTerminal(lo) && !slots.ContainsKey(lo))
                {
                    Push(ref stack, ref top, lo);
                }

                if (!NodeTable.IsTerminal(hi) && !slots.ContainsKey(hi))
                {
                    Push(ref stack, ref top, hi);
                }
            }

            return new NodeOrder(ids.ToArray(), slots);
        }

        /// <summary><paramref name="id"/> の DP 表での添字。</summary>
        /// <param name="id">この並びを作ったときの根から到達できる<b>非終端</b>ノード ID。</param>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="id"/> が並びに入っていない場合（走査か呼び出し側が壊れている）。
        /// </exception>
        public int SlotOf(int id)
        {
            if (!_slots.TryGetValue(id, out int slot))
            {
                // 見つからないときに 0（＝先頭のノードの値）を返すと、誤った DP 値が静かに混ざる。
                ThrowHelper.ThrowInvalidOperationException(
                    $"The node {id} is not reachable from the root this order was built for.");
            }

            return slot;
        }

        private static void Push(ref int[] stack, ref int top, int value)
        {
            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = value;
        }
    }
}

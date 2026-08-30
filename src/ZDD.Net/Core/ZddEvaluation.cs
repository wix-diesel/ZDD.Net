using System;
using ZDD.Net.Internal;

namespace ZDD.Net.Core
{
    /// <summary>
    /// <see cref="IDdEval{TValue}"/> をボトムアップに走らせる評価の入口。
    /// <see cref="Zdd.Count"/> / <see cref="Zdd.CountApprox"/> / <see cref="Zdd.CountBySize"/> は
    /// すべてこの 1 本の走査の上に乗っている。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>なぜ枠組みにするか</b>: ZDD の価値は「10^24 個の解を数える・最適化する・サンプリングする」
    /// ことにあり、それらはどれも <b>DAG 上のボトムアップ DP</b> という同じ形をしている
    /// （docs/PLAN.md §6.4）。違うのは「終端の値」と「合成の仕方」だけなので、走査の側を 1 回だけ書き、
    /// 差分は <see cref="IDdEval{TValue}"/> の実装として与える。利用者が独自の評価
    /// （期待値・多項式・モーメントなど）を書くときも同じ枠組みに乗る。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>。ZDD の深さは変数の個数そのもので、10 万規模の族を素直な再帰で辿ると
    /// <c>StackOverflowException</c> になり、.NET ではこれを catch できずプロセスが即死する
    /// （docs/PLAN.md §4.5）。走査は <see cref="OperationWorkspace"/> の明示スタックによる
    /// ポストオーダーで行う。
    /// </para>
    /// <para>
    /// <b>メモ化はノード ID ごと</b>: 評価値はノードごとに 1 度だけ計算し、その評価の間だけ覚えておく。
    /// 共有されたノードは何人の親から指されていても 1 回しか評価されないので、計算量は
    /// 族の大きさ（集合の個数）ではなく<b>ノード数</b>に比例する。
    /// 覚え先は <see cref="OperationWorkspace"/> の途中結果表（<c>long</c> キー → <c>int</c> 値）で、
    /// そこに入れるのは評価値そのものではなく<b>値表の添字</b>である。表は演算の結果ノード ID を
    /// 入れるために <c>int</c> 固定であり、<c>TValue</c> を直に置けないためで、
    /// こうすると一意化表と同じオープンアドレス法の実装をそのまま使い回せる。
    /// </para>
    /// <para>
    /// <b>演算キャッシュは使わない</b>: <see cref="OperationCache"/> が覚えるのは結果ノード ID
    /// （<c>int</c>）なので、任意の <c>TValue</c> は入らない。したがってメモ化は
    /// 評価 1 回のうちに閉じており、同じ族を 2 度評価すれば 2 度走査される。
    /// </para>
    /// </remarks>
    public static class ZddEvaluation
    {
        /// <summary>評価値を溜める表の初期の大きさ。足りなくなれば倍化する。</summary>
        private const int InitialValueCapacity = 16;

        /// <summary>
        /// 族を <paramref name="eval"/> でボトムアップに評価し、根の値を返す。
        /// </summary>
        /// <typeparam name="TEval">
        /// 評価器の型。<b><c>struct</c> でなければならない</b>（docs/PLAN.md §10-2）。
        /// interface 型で受けるとノードごとの呼び出しが仮想呼び出しになり、数倍遅くなる。
        /// </typeparam>
        /// <typeparam name="TValue">評価値の型。</typeparam>
        /// <param name="zdd">評価する族。</param>
        /// <param name="eval">
        /// 評価器。値渡しで、この呼び出しの中だけで使われる 1 つのコピーに対して
        /// <see cref="IDdEval{TValue}.EvalTerminal"/> / <see cref="IDdEval{TValue}.EvalNode"/> が呼ばれる。
        /// </param>
        /// <returns>根に対する評価値。</returns>
        /// <remarks>
        /// <para>
        /// <typeparamref name="TValue"/> は型引数から推論できない（制約は推論に関与しない）ので、
        /// 呼ぶときは <c>zdd.Evaluate&lt;MyEval, BigInteger&gt;(default)</c> のように
        /// 2 つとも明示する。
        /// </para>
        /// <para>
        /// <b>計算量</b>: 到達できるノード数を <c>m</c> として、
        /// <see cref="IDdEval{TValue}.EvalNode"/> の呼び出しはちょうど <c>m</c> 回。
        /// 走査そのもののアロケーションは評価値表 1 本だけで、作業スタックと途中結果表は
        /// マネージャが持ち回るものを借りて返す。
        /// </para>
        /// <para>
        /// <b>例外</b>: <paramref name="eval"/> が投げた例外はそのまま呼び出し元へ抜ける。
        /// 借りた作業領域はその場合も必ず返される。
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// <paramref name="zdd"/> が <c>default(Zdd)</c> の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// 所有マネージャが破棄済みの場合。
        /// </exception>
        public static TValue Evaluate<TEval, TValue>(this in Zdd zdd, TEval eval)
            where TEval : struct, IDdEval<TValue>
        {
            ZddManager manager = zdd.Manager;

            // 破棄済みならここで ObjectDisposedException になる。
            NodeTable nodes = manager.Table.Nodes;

            // 族の形に依らず 1 度ずつ呼ぶ（IDdEval の約束）。以降は終端に着くたびにこの値を使う。
            TValue falseValue = eval.EvalTerminal(false);
            TValue trueValue = eval.EvalTerminal(true);

            int rootId = zdd.Id;
            if (NodeTable.IsTerminal(rootId))
            {
                return rootId == NodeTable.Top ? trueValue : falseValue;
            }

            // ノードごとの評価値。途中結果表にはこの配列の添字が入る。
            TValue[] values = new TValue[InitialValueCapacity];
            int valueCount = 0;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                work.PushVisit(rootId);

                while (work.TryPop(out long entry))
                {
                    int id = (int)OperationWorkspace.KeyOf(entry);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // 子は必ず計算済み。合成を積んだ直後に子を積んでいるので（LIFO）、
                        // 子の部分木がすべて片付くまで、この項目は取り出されない。
                        // ref は利用者のコード（EvalNode）を挟むと危ないので、先に読み切る。
                        int level;
                        int lo;
                        int hi;
                        {
                            ref ZddNode node = ref nodes[id];
                            level = node.Level;
                            lo = node.Lo;
                            hi = node.Hi;
                        }

                        TValue value = eval.EvalNode(
                            manager.ItemOf(level),
                            ChildValue(work, values, falseValue, trueValue, lo),
                            ChildValue(work, values, falseValue, trueValue, hi));

                        if (valueCount == values.Length)
                        {
                            Array.Resize(ref values, values.Length * 2);
                        }

                        values[valueCount] = value;
                        work.SetResult(id, valueCount);
                        valueCount++;
                        continue;
                    }

                    // 1) 途中結果表: 別の親が既に片付けていれば、それ以上何もしない。
                    if (work.HasResult(id))
                    {
                        continue;
                    }

                    // 2) 1 段降りる。自分を先に積み、その上に未計算の子を積む。
                    //    終端は EvalTerminal の値で即決まるので積まない。
                    int childLo;
                    int childHi;
                    {
                        ref ZddNode node = ref nodes[id];
                        childLo = node.Lo;
                        childHi = node.Hi;
                    }

                    work.PushCombine(id);

                    if (!NodeTable.IsTerminal(childLo) && !work.HasResult(childLo))
                    {
                        work.PushVisit(childLo);
                    }

                    if (!NodeTable.IsTerminal(childHi) && !work.HasResult(childHi))
                    {
                        work.PushVisit(childHi);
                    }
                }

                if (!work.TryGetResult(rootId, out int slot))
                {
                    // 根は非終端なので必ず合成を通っている。ここに来るのは走査が壊れたときだけで、
                    // 見つからないときの TryGetResult は 0（＝値表の先頭）を返すため、
                    // 確かめずに読むと「別のノードの値」を静かに返してしまう。
                    ThrowHelper.ThrowInvalidOperationException(
                        $"The evaluation of node {rootId} finished without producing a value.");
                }

                return values[slot];
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>
        /// 子の評価値を引く。終端なら <see cref="IDdEval{TValue}.EvalTerminal"/> の値、
        /// そうでなければ値表から引く（子は必ず計算済み）。
        /// </summary>
        private static TValue ChildValue<TValue>(
            OperationWorkspace work,
            TValue[] values,
            TValue falseValue,
            TValue trueValue,
            int childId)
        {
            if (NodeTable.IsTerminal(childId))
            {
                return childId == NodeTable.Top ? trueValue : falseValue;
            }

            if (!work.TryGetResult(childId, out int slot))
            {
                // 子は親の合成より先に片付いているはず。見つからないときの TryGetResult は
                // 0（＝値表の先頭）を返すので、確かめずに読むと誤った値で計算が進んでしまう。
                ThrowHelper.ThrowInvalidOperationException(
                    $"The child node {childId} was evaluated after its parent instead of before it.");
            }

            return values[slot];
        }
    }
}

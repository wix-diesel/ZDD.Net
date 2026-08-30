using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace ZDD.Net.Core
{
    /// <summary>
    /// item を 1 つ取る単項演算（<see cref="ZddOperation.Change"/> /
    /// <see cref="ZddOperation.OnSet"/> / <see cref="ZddOperation.OffSet"/>）の実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>この型は反復実装の雛形でもある</b>（docs/PLAN.md §4.5）。M1-7 以降の演算
    /// （集合演算・積商剰余・包含系・極大極小）は <see cref="Apply"/> の形をそのまま写して書く。
    /// 変わるのは「基底ケース」と「合成の仕方」だけで、スタックの回し方は同じである。
    /// 骨格の説明は <see cref="OperationWorkspace"/> にある。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>。ZDD の深さは変数の個数そのもので、10 万規模の族を素直な再帰で辿ると
    /// <c>StackOverflowException</c> になり、.NET ではこれを catch できずプロセスが即死する。
    /// この型に再帰呼び出しが 1 つも無いことは、レビュー観点チェックリストの第 1 項
    /// （docs/ROADMAP.md）にあたる。
    /// </para>
    /// <para>
    /// <b>3 つの演算をまとめて書く理由</b>: 3 つとも「item のレベルに達するまで素通りし、
    /// 達したところで枝を組み替える」という同じ形をしていて、違うのは基底ケースの数行だけである。
    /// 演算ごとに同じループを 3 回書くほうが、食い違いを招きやすい。
    /// </para>
    /// </remarks>
    internal static class UnaryOperations
    {
        /// <summary>
        /// <paramref name="rootId"/> を根とする族に単項演算を適用し、結果の根ノード ID を返す。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="op">
        /// <see cref="ZddOperation.Change"/> / <see cref="ZddOperation.OnSet"/> /
        /// <see cref="ZddOperation.OffSet"/> のいずれか。
        /// </param>
        /// <param name="rootId">入力の族の根ノード ID。</param>
        /// <param name="item">演算の対象となる item index。</param>
        /// <returns>結果の族の根ノード ID。</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="item"/> が <paramref name="manager"/> の変数の範囲外の場合。
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static int Apply(ZddManager manager, ZddOperation op, int rootId, int item)
        {
            Debug.Assert(
                op is ZddOperation.Change or ZddOperation.OnSet or ZddOperation.OffSet,
                $"'{op}' is not one of the item-taking unary operations.");

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            // item の範囲検査を兼ねる。以降 level はこの演算のあいだ変わらない。
            int level = manager.LevelOf(item);

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
                        // ref は GetNode（ノード表を伸ばしうる）を挟むと古い配列を指しうるため、
                        // 必要な値をここで読み切ってしまう。
                        int nodeLevel;
                        int childLo;
                        int childHi;
                        {
                            ref ZddNode node = ref nodes[id];
                            nodeLevel = node.Level;
                            childLo = node.Lo;
                            childHi = node.Hi;
                        }

                        work.TryGetResult(childLo, out int loResult);
                        work.TryGetResult(childHi, out int hiResult);

                        // ゼロサプレス規則と一意化は GetNode が引き受けるので、ここでは規則を意識しない。
                        int combined = table.GetNode(nodeLevel, loResult, hiResult);
                        work.SetResult(id, combined);
                        cache.PutUnary(op, id, item, combined);
                        continue;
                    }

                    // 1) 途中結果表: 別の親が既に片付けていれば、それ以上何もしない。
                    if (work.HasResult(id))
                    {
                        continue;
                    }

                    // 2) 基底ケース: item のレベル以下まで降りたら、そこで答が決まる。
                    int currentLevel = NodeTable.IsTerminal(id) ? 0 : nodes[id].Level;
                    if (currentLevel <= level)
                    {
                        work.SetResult(id, BaseCase(table, nodes, op, id, currentLevel, level));
                        continue;
                    }

                    // 3) 演算キャッシュ: 過去の演算で同じ部分問題を解いていれば、その答を使う。
                    //    基底ケースの後に見るのは、基底ケースのほうが引くより安いからである。
                    if (cache.TryGetUnary(op, id, item, out int cached))
                    {
                        work.SetResult(id, cached);
                        continue;
                    }

                    // 4) item のレベルより上のノードは素通し。両方の子に同じ演算をかけて組み直す。
                    //    自分を先に積み、その上に未計算の子を積む。
                    int lo;
                    int hi;
                    {
                        ref ZddNode node = ref nodes[id];
                        lo = node.Lo;
                        hi = node.Hi;
                    }

                    work.PushCombine(id);

                    if (!work.HasResult(lo))
                    {
                        work.PushVisit(lo);
                    }

                    if (!work.HasResult(hi))
                    {
                        work.PushVisit(hi);
                    }
                }

                work.TryGetResult(rootId, out int result);
                return result;
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>
        /// item のレベルに達した（か、それより下まで降りた）ノードに対する答を返す。
        /// ここだけが演算ごとに違う。
        /// </summary>
        /// <param name="table">ノードの生成に使う一意化表。</param>
        /// <param name="nodes">ノード表。</param>
        /// <param name="op">演算の種別。</param>
        /// <param name="id">対象ノードの ID。</param>
        /// <param name="currentLevel"><paramref name="id"/> のレベル（終端なら 0）。</param>
        /// <param name="level">対象 item のレベル。</param>
        private static int BaseCase(
            UniqueTable table,
            NodeTable nodes,
            ZddOperation op,
            int id,
            int currentLevel,
            int level)
        {
            Debug.Assert(currentLevel <= level, "BaseCase is only reached at or below the item's level.");

            if (currentLevel < level)
            {
                // この族は item に一度も言及していない = どの集合も item を含まない。
                return op switch
                {
                    // 全部の集合に item を足す。id が ⊥ なら GetNode がゼロサプレス規則で ⊥ を返す。
                    ZddOperation.Change => table.GetNode(level, NodeTable.Bottom, id),

                    // item を含む集合は 1 つも無い。
                    ZddOperation.OnSet => NodeTable.Bottom,

                    // どの集合も item を含まないので、族はそのまま。
                    ZddOperation.OffSet => id,

                    _ => ThrowUnsupported(op),
                };
            }

            // currentLevel == level。ここが item そのものの分岐で、
            // lo = item を含まない側、hi = item を含む側から item を除いたもの。
            int lo;
            int hi;
            {
                ref ZddNode node = ref nodes[id];
                lo = node.Lo;
                hi = node.Hi;
            }

            return op switch
            {
                // 含む／含まないを入れ替える。lo が ⊥ なら（＝全集合が item を含むなら）
                // GetNode がゼロサプレス規則で hi をそのまま返す。
                ZddOperation.Change => table.GetNode(level, hi, lo),

                // item を含む側を取り出し、item を除く。
                ZddOperation.OnSet => hi,

                // item を含まない側を取り出す。
                ZddOperation.OffSet => lo,

                _ => ThrowUnsupported(op),
            };
        }

        [DoesNotReturn]
        private static int ThrowUnsupported(ZddOperation op) =>
            throw new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the item-taking unary operations (Change / OnSet / OffSet).");
    }
}

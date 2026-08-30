using System;
using System.Diagnostics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 族どうしを組み合わせる集合演算（<see cref="ZddOperation.Union"/> /
    /// <see cref="ZddOperation.Intersect"/> / <see cref="ZddOperation.Difference"/> /
    /// <see cref="ZddOperation.SymmetricDifference"/>）の実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>単項演算の雛形をそのまま二項に広げたもの</b>（<see cref="UnaryOperations.Apply"/> /
    /// docs/PLAN.md §4.5）。スタックの回し方は同じで、変わるのは
    /// 「部分問題が 1 つのノードではなく<b>ノードの対</b>になる」ところだけである。
    /// 対は <see cref="OperationKey.Of"/> で 1 個の <c>long</c> に詰め、
    /// 作業スタックと途中結果表にはその値を入れる。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>。ZDD の深さは変数の個数そのもので、10 万規模の族を素直な再帰で辿ると
    /// <c>StackOverflowException</c> になり、.NET ではこれを catch できずプロセスが即死する。
    /// </para>
    /// <para>
    /// <b>分解の形</b>（<see cref="Decompose"/>）: レベル <c>L</c> のノードは
    /// <c>f = f₀ ∪ item·f₁</c> と読める（<c>f₀</c> = item を含まない側、
    /// <c>f₁</c> = item を含む側から item を除いたもの）。両者が同じレベルで分岐していれば
    /// 枝どうしを突き合わせればよく、片方だけが上にあるときは
    /// <b>下の族には その item を含む集合が 1 つも無い</b>ので、
    /// 上の族の 1-枝をそのまま残すか丸ごと捨てるかのどちらかになる。
    /// 演算ごとに違うのはその 1 点だけなので、4 演算を 1 つのループで書いている。
    /// </para>
    /// <para>
    /// <b>可換演算のキー</b>: オペランドの正規化は <see cref="OperationKey"/> が引き受ける。
    /// </para>
    /// </remarks>
    internal static class BinaryOperations
    {
        /// <summary>ノードを作らず Lo 枝の答をそのまま結果にすることを表す番兵のレベル。</summary>
        /// <remarks>レベル 0 は終端のもので、演算がノードを作るレベルには決してならない。</remarks>
        private const int NoNode = 0;

        /// <summary>
        /// 2 つの族に集合演算を適用し、結果の根ノード ID を返す。
        /// </summary>
        /// <param name="manager">両方の族を所有するマネージャ。</param>
        /// <param name="op">
        /// <see cref="ZddOperation.Union"/> / <see cref="ZddOperation.Intersect"/> /
        /// <see cref="ZddOperation.Difference"/> / <see cref="ZddOperation.SymmetricDifference"/>
        /// のいずれか。
        /// </param>
        /// <param name="fRoot">左オペランドの根ノード ID。</param>
        /// <param name="gRoot">右オペランドの根ノード ID。</param>
        /// <returns>結果の族の根ノード ID。</returns>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static int Apply(ZddManager manager, ZddOperation op, int fRoot, int gRoot)
        {
            Debug.Assert(
                op is ZddOperation.Union
                    or ZddOperation.Intersect
                    or ZddOperation.Difference
                    or ZddOperation.SymmetricDifference,
                $"'{op}' is not one of the set operations.");

            // 終端どうし（∅ / {∅} の組合せ）と、片方が ∅ の場合はここで片付く。
            // 作業領域を借りる前に返せるので、単発の f | Empty のような呼び出しは表に触れない。
            if (TryResolveTerminal(op, fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(op, fRoot, gRoot);
                work.PushVisit(rootKey);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    int f = OperationKey.LeftOf(key);
                    int g = OperationKey.RightOf(key);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // 子は必ず計算済み。合成を積んだ直後に子を積んでいるので（LIFO）、
                        // 子の部分問題がすべて片付くまで、この項目は取り出されない。
                        // 分解はノード表を読むだけなので、積んだときと同じ答が出る。
                        Decompose(op, nodes, f, g, out int level, out long loKey, out long hiKey, out int hiId);

                        work.TryGetResult(loKey, out int loResult);

                        int combined;
                        if (level == NoNode)
                        {
                            // 上の族の 1-枝が丸ごと落ちる演算。残るのは 0-枝側の答だけ。
                            combined = loResult;
                        }
                        else
                        {
                            // Hi 枝は「部分問題の答」か「そのまま残る既存のノード」のどちらか。
                            int hiResult = hiId;
                            if (hiKey != OperationKey.None)
                            {
                                work.TryGetResult(hiKey, out hiResult);
                            }

                            // ゼロサプレス規則と一意化は GetNode が引き受ける。
                            combined = table.GetNode(level, loResult, hiResult);
                        }

                        work.SetResult(key, combined);
                        cache.PutBinary(op, f, g, combined);
                        continue;
                    }

                    // 1) 途中結果表: 別の親が既に片付けていれば、それ以上何もしない。
                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    // 2) 基底ケース: 終端が絡む組合せは、降りずにその場で答が決まる。
                    if (TryResolveTerminal(op, f, g, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    // 3) 演算キャッシュ: 過去の演算で同じ部分問題を解いていれば、その答を使う。
                    if (cache.TryGetBinary(op, f, g, out int cached))
                    {
                        work.SetResult(key, cached);
                        continue;
                    }

                    // 4) 1 段降りる。自分を先に積み、その上に未計算の子を積む。
                    Decompose(op, nodes, f, g, out _, out long childLoKey, out long childHiKey, out _);

                    work.PushCombine(key);

                    if (!work.HasResult(childLoKey))
                    {
                        work.PushVisit(childLoKey);
                    }

                    if (childHiKey != OperationKey.None && !work.HasResult(childHiKey))
                    {
                        work.PushVisit(childHiKey);
                    }
                }

                work.TryGetResult(rootKey, out int result);
                return result;
            }
            finally
            {
                manager.ReturnWorkspace(work);
            }
        }

        /// <summary>
        /// 終端が絡む組合せの答を返す。ここだけで <c>f == g</c> / <c>f == ∅</c> / <c>g == ∅</c> を扱う。
        /// </summary>
        /// <returns>答が決まれば <see langword="true"/>。</returns>
        /// <remarks>
        /// <para>
        /// この 3 つで<b>終端どうしの組合せはすべて尽きる</b>。残るのは <c>{∅}</c> と ∅ の対だけで、
        /// それは <c>g == ∅</c>（または <c>f == ∅</c>）に当たるからである。
        /// </para>
        /// <para>
        /// <c>{∅}</c>（<see cref="ZddManager.Base"/>）は、これら 4 演算では定数時間の近道にならない。
        /// たとえば <c>f ∪ {∅}</c> は「f に空集合を足す」演算で、f の 0-枝を末端まで辿らなければ
        /// 答が決まらない。近道を作るには結局同じだけ降りることになるので、素直に分解に任せる。
        /// </para>
        /// </remarks>
        private static bool TryResolveTerminal(ZddOperation op, int f, int g, out int result)
        {
            if (f == g)
            {
                // f ∪ f = f ∩ f = f、f ∖ f = f △ f = ∅。
                result = op is ZddOperation.Union or ZddOperation.Intersect ? f : NodeTable.Bottom;
                return true;
            }

            if (f == NodeTable.Bottom)
            {
                // ∅ ∪ g = ∅ △ g = g、∅ ∩ g = ∅ ∖ g = ∅。
                result = op is ZddOperation.Union or ZddOperation.SymmetricDifference ? g : NodeTable.Bottom;
                return true;
            }

            if (g == NodeTable.Bottom)
            {
                // f ∪ ∅ = f △ ∅ = f ∖ ∅ = f、f ∩ ∅ = ∅。
                result = op == ZddOperation.Intersect ? NodeTable.Bottom : f;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>
        /// 部分問題 <c>(f, g)</c> を 1 段分解する。ここだけが演算ごとに違う。
        /// </summary>
        /// <param name="op">演算の種別。</param>
        /// <param name="nodes">ノード表。</param>
        /// <param name="f">左オペランドのノード ID。</param>
        /// <param name="g">右オペランドのノード ID。</param>
        /// <param name="level">
        /// 合成で作るノードのレベル。<see cref="NoNode"/> なら<b>ノードを作らず</b>
        /// <paramref name="loKey"/> の答をそのまま結果にする。
        /// </param>
        /// <param name="loKey">0-枝側の部分問題のキー。常に有効。</param>
        /// <param name="hiKey">
        /// 1-枝側の部分問題のキー。<see cref="OperationKey.None"/> なら 1-枝は部分問題ではなく
        /// <paramref name="hiId"/> のノードがそのまま入る。
        /// </param>
        /// <param name="hiId"><paramref name="hiKey"/> が <see cref="OperationKey.None"/> のときの 1-枝のノード ID。</param>
        private static void Decompose(
            ZddOperation op,
            NodeTable nodes,
            int f,
            int g,
            out int level,
            out long loKey,
            out long hiKey,
            out int hiId)
        {
            Debug.Assert(
                !NodeTable.IsTerminal(f) || !NodeTable.IsTerminal(g),
                "A pair of terminals is always settled by TryResolveTerminal and never reaches Decompose.");

            int fLevel = NodeTable.IsTerminal(f) ? 0 : nodes[f].Level;
            int gLevel = NodeTable.IsTerminal(g) ? 0 : nodes[g].Level;

            if (fLevel == gLevel)
            {
                // 同じ item で分岐している。0-枝どうし・1-枝どうしを突き合わせる。
                int fLo;
                int fHi;
                {
                    ref ZddNode node = ref nodes[f];
                    fLo = node.Lo;
                    fHi = node.Hi;
                }

                int gLo;
                int gHi;
                {
                    ref ZddNode node = ref nodes[g];
                    gLo = node.Lo;
                    gHi = node.Hi;
                }

                level = fLevel;
                loKey = OperationKey.Of(op, fLo, gLo);
                hiKey = OperationKey.Of(op, fHi, gHi);
                hiId = NodeTable.Bottom;
                return;
            }

            // 片方だけが上（根側）にある。下の族はその item に一度も言及していない
            // ＝ どの集合もその item を含まないので、上の族の 1-枝と交わる集合は無い。
            bool fIsUpper = fLevel > gLevel;
            int upper = fIsUpper ? f : g;
            int lower = fIsUpper ? g : f;

            int upperLo;
            int upperHi;
            {
                ref ZddNode node = ref nodes[upper];
                upperLo = node.Lo;
                upperHi = node.Hi;
            }

            // 上の族の 1-枝（= その item を含む集合たち）が答に残るかどうか。
            bool keepsUpperHi = op switch
            {
                // どちらも「相手が持っていない集合」を残す演算なので、1-枝はそのまま通る。
                ZddOperation.Union or ZddOperation.SymmetricDifference => true,

                // 相手に同じ集合は無いので、1-枝は丸ごと落ちる。
                ZddOperation.Intersect => false,

                // f ∖ g: f が上なら f の 1-枝は g に削られず残る。
                // g が上なら g の 1-枝は f から何も削れないので、g の 0-枝だけを見ればよい。
                ZddOperation.Difference => fIsUpper,

                _ => throw Unsupported(op),
            };

            // 差だけが非可換なので、上下を入れ替えても左右の並びは崩さない。
            loKey = fIsUpper ? OperationKey.Of(op, upperLo, lower) : OperationKey.Of(op, lower, upperLo);
            hiKey = OperationKey.None;

            if (keepsUpperHi)
            {
                level = fIsUpper ? fLevel : gLevel;
                hiId = upperHi;
            }
            else
            {
                level = NoNode;
                hiId = NodeTable.Bottom;
            }
        }

        private static ArgumentOutOfRangeException Unsupported(ZddOperation op) =>
            new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the set operations (Union / Intersect / Difference / SymmetricDifference).");
    }
}

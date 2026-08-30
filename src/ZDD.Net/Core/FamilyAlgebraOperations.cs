using System;
using System.Diagnostics;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 族を多項式のように掛け割りする演算（<see cref="ZddOperation.Product"/> /
    /// <see cref="ZddOperation.Quotient"/> / <see cref="ZddOperation.Remainder"/>）の実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>集合演算との違い</b>: <see cref="BinaryOperations"/> の 4 演算は、部分問題の答を
    /// そのまま枝に差せば済む（答は必ず「1 個のノードを作る」か「0-枝の答をそのまま返す」かのどちらか）。
    /// 積と商はそうならない。積の 1-枝は<b>3 つの部分積の和</b>で、商の合成には<b>積の交わり</b>が要る。
    /// つまり合成のたびに<b>別の演算</b>（<see cref="ZddOperation.Union"/> /
    /// <see cref="ZddOperation.Intersect"/>）を 1 回呼ぶことになる。
    /// 呼ばれた側は自前の作業領域を借りて回るので（<see cref="ZddManager.RentWorkspace"/> は
    /// 入れ子の深さぶんを使い回す）、こちらのスタックと混ざることはない。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>。ZDD の深さは変数の個数そのもので、10 万規模の族を素直な再帰で辿ると
    /// <c>StackOverflowException</c> になり、.NET ではこれを catch できずプロセスが即死する。
    /// 入れ子で呼ぶ集合演算も反復実装なので、この経路にネイティブスタックを深く食う箇所は無い
    /// （入れ子の深さは「積 → 和」「商 → 交わり」の 1 段だけで、族の大きさに依らない）。
    /// </para>
    /// <para>
    /// <b>分解の形</b>: レベル <c>v</c> で <c>f = f₀ ∪ v·f₁</c>、<c>g = g₀ ∪ v·g₁</c> と読むと
    /// （<c>v·X</c> は「X の各集合に item を足したもの」）、
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>積</b>: <c>f * g = (f₀ * g₀) ∪ v·((f₁ * g₁) ∪ (f₁ * g₀) ∪ (f₀ * g₁))</c>。
    /// item を含む集合どうしを足しても item は 1 個のままなので、1-枝に 3 項が集まる。
    /// </description></item>
    /// <item><description>
    /// <b>商</b>: <c>v</c> を <b>g の最上位 item</b> に取ると、ゼロサプレス規則より <c>g₁ ≠ ∅</c>、
    /// すなわち g には <c>v</c> を含む集合が必ずある。商の元は g のどの集合とも交わらないので
    /// <c>v</c> を含めず、<c>f / g = (f₁ / g₁) ∩ (f₀ / g₀)</c>（<c>g₀ = ∅</c> なら <c>f₁ / g₁</c>）。
    /// f のほうが上にあるとき（f の最上位 item が <c>v</c> より根側）は、その item は g に現れないので
    /// 商に残ってよく、<c>f / g = (f₀ / g) ∪ v·(f₁ / g)</c> と 1 段そのまま降ろせる。
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>剰余</b>は定義式 <c>f % g = f ∖ (g * (f / g))</c> をそのまま組み立てる（docs/PLAN.md §5.2）。
    /// 独自の走査を書いても商と積を作り直すだけなので、3 つの演算を並べたほうが定義との対応が見える。
    /// </para>
    /// </remarks>
    internal static class FamilyAlgebraOperations
    {
        /// <summary>
        /// 2 つの族に積・商・剰余のいずれかを適用し、結果の根ノード ID を返す。
        /// </summary>
        /// <param name="manager">両方の族を所有するマネージャ。</param>
        /// <param name="op">
        /// <see cref="ZddOperation.Product"/> / <see cref="ZddOperation.Quotient"/> /
        /// <see cref="ZddOperation.Remainder"/> のいずれか。
        /// </param>
        /// <param name="fRoot">左オペランドの根ノード ID。</param>
        /// <param name="gRoot">右オペランドの根ノード ID。</param>
        /// <returns>結果の族の根ノード ID。</returns>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static int Apply(ZddManager manager, ZddOperation op, int fRoot, int gRoot) =>
            op switch
            {
                ZddOperation.Product => Product(manager, fRoot, gRoot),
                ZddOperation.Quotient => Quotient(manager, fRoot, gRoot),
                ZddOperation.Remainder => Remainder(manager, fRoot, gRoot),
                _ => throw Unsupported(op),
            };

        // ---- 積 ----

        /// <summary>
        /// 積 <c>f * g = { a ∪ b : a ∈ f, b ∈ g }</c> を求める。
        /// </summary>
        private static int Product(ZddManager manager, int fRoot, int gRoot)
        {
            // 終端が絡む組合せはここで片付く。作業領域を借りる前に返せるので、
            // 単発の f * Base のような呼び出しは表に触れない。
            if (TryResolveProduct(fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(ZddOperation.Product, fRoot, gRoot);
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
                        Split(nodes, f, g, out int level, out int f0, out int f1, out int g0, out int g1);

                        int lo = ProductOf(work, f0, g0);

                        // 1-枝は 3 つの部分積の和。∅ が混ざる呼び出しは Union 側の終端処理が
                        // 作業領域を借りずに返すので、ここで場合分けはしない
                        // （レベルが食い違う分解では 2 項が ∅ になり、和は実質素通しになる）。
                        int hi = ProductOf(work, f1, g1);
                        hi = Combine(manager, ZddOperation.Union, hi, ProductOf(work, f1, g0));
                        hi = Combine(manager, ZddOperation.Union, hi, ProductOf(work, f0, g1));

                        // ゼロサプレス規則と一意化は GetNode が引き受ける。
                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(key, combined);
                        cache.PutBinary(ZddOperation.Product, f, g, combined);
                        continue;
                    }

                    // 1) 途中結果表: 別の親が既に片付けていれば、それ以上何もしない。
                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    // 2) 基底ケース: 終端が絡む組合せは、降りずにその場で答が決まる。
                    if (TryResolveProduct(f, g, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    // 3) 演算キャッシュ: 過去の演算で同じ部分問題を解いていれば、その答を使う。
                    if (cache.TryGetBinary(ZddOperation.Product, f, g, out int cached))
                    {
                        work.SetResult(key, cached);
                        continue;
                    }

                    // 4) 1 段降りる。自分を先に積み、その上に未計算の子を積む。
                    Split(nodes, f, g, out _, out int childF0, out int childF1, out int childG0, out int childG1);

                    work.PushCombine(key);
                    PushProduct(work, childF0, childG0);
                    PushProduct(work, childF1, childG1);
                    PushProduct(work, childF1, childG0);
                    PushProduct(work, childF0, childG1);
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
        /// 終端が絡む積の答を返す。<c>∅ * g = ∅</c>、<c>{∅} * g = g</c> の 2 つで尽きる。
        /// </summary>
        /// <returns>答が決まれば <see langword="true"/>。</returns>
        /// <remarks>
        /// <c>f == g</c> は近道にならない。<c>f * f</c> は <c>{ a ∪ b : a, b ∈ f }</c> であって
        /// f 自身ではない（<c>{{0}, {1}} * {{0}, {1}} = {{0}, {1}, {0, 1}}</c>）。
        /// </remarks>
        private static bool TryResolveProduct(int f, int g, out int result)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                // 相手が 1 つも集合を持たないので、作れる和も 1 つも無い。
                result = NodeTable.Bottom;
                return true;
            }

            if (f == NodeTable.Top)
            {
                // {∅} は積の単位元。a ∪ ∅ = a。
                result = g;
                return true;
            }

            if (g == NodeTable.Top)
            {
                result = f;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>
        /// 部分積 <c>(f, g)</c> を積む。∅ が絡む対はその場で答が決まるので、表にも積まない。
        /// </summary>
        /// <remarks>
        /// <see cref="ProductOf"/> と対になっていて、<b>同じ条件で同じキーを作る</b>こと。
        /// 積むときと読むときで判定がずれると、合成が別の部分問題の答を拾う。
        /// </remarks>
        private static void PushProduct(OperationWorkspace work, int f, int g)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                return;
            }

            long key = OperationKey.Of(ZddOperation.Product, f, g);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>計算済みの部分積 <c>(f, g)</c> の答。<see cref="PushProduct"/> と対になっている。</summary>
        private static int ProductOf(OperationWorkspace work, int f, int g)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                return NodeTable.Bottom;
            }

            work.TryGetResult(OperationKey.Of(ZddOperation.Product, f, g), out int result);
            return result;
        }

        // ---- 商 ----

        /// <summary>
        /// 商 <c>f / g = { a : ∀ b ∈ g, a ∩ b = ∅ かつ a ∪ b ∈ f }</c> を求める。
        /// </summary>
        /// <remarks>
        /// <c>g == ∅</c> だけは走査に乗らない（条件が空虚に真になり、答が全部分集合になる）ので、
        /// 入口で冪集合を組み立てて返す。以降の部分問題に <c>g == ∅</c> は現れない
        /// （降ろすのは <c>g</c> そのものか、∅ でないと確かめた <c>g₀</c> / <c>g₁</c> だけ）。
        /// </remarks>
        private static int Quotient(ZddManager manager, int fRoot, int gRoot)
        {
            if (gRoot == NodeTable.Bottom)
            {
                return PowerSet(manager);
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            if (TryResolveQuotient(nodes, fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(ZddOperation.Quotient, fRoot, gRoot);
                work.PushVisit(rootKey);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    int f = OperationKey.LeftOf(key);
                    int g = OperationKey.RightOf(key);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // 子は必ず計算済み。分解はノード表を読むだけなので、積んだときと同じ形になる。
                        Split(nodes, f, g, out int level, out int f0, out int f1, out int g0, out int g1);

                        int combined;
                        if (IsAboveDivisor(nodes, f, g))
                        {
                            // g に現れない item なので、商に残ってよい。1 段そのまま降ろす。
                            combined = table.GetNode(
                                level,
                                QuotientOf(work, f0, g),
                                QuotientOf(work, f1, g));
                        }
                        else
                        {
                            // g の最上位 item。商の元はそれを含めないので、ノードは作らない。
                            combined = QuotientOf(work, f1, g1);

                            if (g0 != NodeTable.Bottom)
                            {
                                // item を含まない側の割り算も同時に満たす必要がある。
                                combined = Combine(
                                    manager,
                                    ZddOperation.Intersect,
                                    combined,
                                    QuotientOf(work, f0, g0));
                            }
                        }

                        work.SetResult(key, combined);
                        cache.PutBinary(ZddOperation.Quotient, f, g, combined);
                        continue;
                    }

                    // 1) 途中結果表 → 2) 基底ケース → 3) 演算キャッシュ の順に見る。
                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    if (TryResolveQuotient(nodes, f, g, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    if (cache.TryGetBinary(ZddOperation.Quotient, f, g, out int cached))
                    {
                        work.SetResult(key, cached);
                        continue;
                    }

                    // 4) 1 段降りる。自分を先に積み、その上に未計算の子を積む。
                    Split(nodes, f, g, out _, out int childF0, out int childF1, out int childG0, out int childG1);

                    work.PushCombine(key);

                    if (IsAboveDivisor(nodes, f, g))
                    {
                        PushQuotient(work, childF0, g);
                        PushQuotient(work, childF1, g);
                    }
                    else
                    {
                        PushQuotient(work, childF1, childG1);

                        if (childG0 != NodeTable.Bottom)
                        {
                            PushQuotient(work, childF0, childG0);
                        }
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
        /// 終端や、割られる族が浅すぎる組合せの答を返す。<c>g == ∅</c> はここには来ない。
        /// </summary>
        /// <returns>答が決まれば <see langword="true"/>。</returns>
        private static bool TryResolveQuotient(NodeTable nodes, int f, int g, out int result)
        {
            Debug.Assert(g != NodeTable.Bottom, "Division by the empty family is settled before the walk starts.");

            if (g == NodeTable.Top)
            {
                // f / {∅} = f。a ∪ ∅ = a なので、条件は「a ∈ f」だけになる。
                result = f;
                return true;
            }

            if (f == NodeTable.Bottom)
            {
                // ∅ / g = ∅。g には集合が 1 つ以上あるのに、和の行き先が無い。
                result = NodeTable.Bottom;
                return true;
            }

            if (f == g)
            {
                // f / f = {∅}。∅ は必ず商に入り、空でない a を採ると
                // a ∪ b ∈ f が b より真に大きい集合を要求し続けるので、有限の族では行き詰まる。
                result = NodeTable.Top;
                return true;
            }

            // ここで g は非終端。f が g の最上位 item より下（葉側）にしかないなら、
            // f には その item を含む集合が 1 つも無いので、a ∪ b ∈ f を満たす a は無い。
            int fLevel = NodeTable.IsTerminal(f) ? 0 : nodes[f].Level;
            if (fLevel < nodes[g].Level)
            {
                result = NodeTable.Bottom;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>
        /// <paramref name="f"/> の最上位 item が、割る族 <paramref name="g"/> のそれより根側かどうか。
        /// 真なら「1 段そのまま降ろす」側、偽なら「g の最上位 item での割り算」側になる。
        /// </summary>
        /// <remarks>
        /// 積むときと合成のときで同じ判定を使う。どちらも <paramref name="f"/> と <paramref name="g"/> は
        /// 非終端（基底ケースで弾かれている）。
        /// </remarks>
        private static bool IsAboveDivisor(NodeTable nodes, int f, int g) => nodes[f].Level > nodes[g].Level;

        /// <summary>部分商 <c>(f, g)</c> を積む。</summary>
        private static void PushQuotient(OperationWorkspace work, int f, int g)
        {
            long key = OperationKey.Of(ZddOperation.Quotient, f, g);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>計算済みの部分商 <c>(f, g)</c> の答。<see cref="PushQuotient"/> と対になっている。</summary>
        private static int QuotientOf(OperationWorkspace work, int f, int g)
        {
            work.TryGetResult(OperationKey.Of(ZddOperation.Quotient, f, g), out int result);
            return result;
        }

        // ---- 剰余 ----

        /// <summary>
        /// 剰余 <c>f % g = f ∖ (g * (f / g))</c> を求める。
        /// </summary>
        /// <remarks>
        /// 定義式のとおりに商・積・差を組み合わせる。根の答だけは
        /// <see cref="ZddOperation.Remainder"/> としてキャッシュに載せるので、
        /// 同じ対を繰り返し割ったときに 3 つの演算を通り直さずに済む。
        /// </remarks>
        private static int Remainder(ZddManager manager, int fRoot, int gRoot)
        {
            if (gRoot == NodeTable.Top)
            {
                // f % {∅} = f ∖ ({∅} * f) = ∅。割り切れる。
                return NodeTable.Bottom;
            }

            if (gRoot == NodeTable.Bottom || fRoot == NodeTable.Bottom)
            {
                // 商が何であれ ∅ を掛ければ ∅ なので、f % ∅ = f。
                // f が ∅ なら引かれる側が無いので、やはり f（= ∅）。
                return fRoot;
            }

            OperationCache cache = manager.Cache;
            if (cache.TryGetBinary(ZddOperation.Remainder, fRoot, gRoot, out int cached))
            {
                return cached;
            }

            int quotient = Quotient(manager, fRoot, gRoot);
            int divisible = Product(manager, gRoot, quotient);
            int result = BinaryOperations.Apply(manager, ZddOperation.Difference, fRoot, divisible);

            cache.PutBinary(ZddOperation.Remainder, fRoot, gRoot, result);
            return result;
        }

        // ---- 共通の道具 ----

        /// <summary>
        /// 部分問題 <c>(f, g)</c> を、両者の最上位 item のうち根側のレベルで 1 段分解する。
        /// </summary>
        /// <param name="nodes">ノード表。</param>
        /// <param name="f">左オペランドのノード ID。</param>
        /// <param name="g">右オペランドのノード ID。</param>
        /// <param name="level">分解したレベル（1 以上）。</param>
        /// <param name="f0"><paramref name="f"/> のうち item を含まない側。</param>
        /// <param name="f1"><paramref name="f"/> のうち item を含む側から、item を除いたもの。</param>
        /// <param name="g0"><paramref name="g"/> のうち item を含まない側。</param>
        /// <param name="g1"><paramref name="g"/> のうち item を含む側から、item を除いたもの。</param>
        /// <remarks>
        /// 片方だけが上（根側）にあるときは、下の族はその item に一度も言及していない
        /// ＝ どの集合も item を含まないので、0-枝がその族自身、1-枝が ∅ になる。
        /// ノード表への <c>ref</c> は持ち出さない（<see cref="UniqueTable.GetNode"/> が
        /// 表を伸ばすと古い配列を指しうるため、必要な値をここで読み切る）。
        /// </remarks>
        private static void Split(
            NodeTable nodes,
            int f,
            int g,
            out int level,
            out int f0,
            out int f1,
            out int g0,
            out int g1)
        {
            Debug.Assert(
                !NodeTable.IsTerminal(f) || !NodeTable.IsTerminal(g),
                "A pair of terminals is always settled by the base case and never reaches Split.");

            int fLevel = NodeTable.IsTerminal(f) ? 0 : nodes[f].Level;
            int gLevel = NodeTable.IsTerminal(g) ? 0 : nodes[g].Level;

            level = Math.Max(fLevel, gLevel);

            if (fLevel == level)
            {
                ref ZddNode node = ref nodes[f];
                f0 = node.Lo;
                f1 = node.Hi;
            }
            else
            {
                f0 = f;
                f1 = NodeTable.Bottom;
            }

            if (gLevel == level)
            {
                ref ZddNode node = ref nodes[g];
                g0 = node.Lo;
                g1 = node.Hi;
            }
            else
            {
                g0 = g;
                g1 = NodeTable.Bottom;
            }
        }

        /// <summary>
        /// 合成の途中で別の演算（和・交わり）を 1 回かける。呼ばれた側は自分の作業領域を借りるので、
        /// こちらのスタックには影響しない。
        /// </summary>
        private static int Combine(ZddManager manager, ZddOperation op, int f, int g) =>
            BinaryOperations.Apply(manager, op, f, g);

        /// <summary>
        /// 全体集合の冪集合 <c>2^U</c>（<see cref="ZddManager.VariableCount"/> 個の item の全部分集合）。
        /// </summary>
        /// <remarks>
        /// どの item も「入れても入れなくてもよい」ので、各レベルで 0-枝と 1-枝が同じ族を指す。
        /// ノードは変数の個数ぶんだけで、族としての大きさ（2^n 個の集合）とは無関係に小さい。
        /// </remarks>
        private static int PowerSet(ZddManager manager)
        {
            UniqueTable table = manager.Table;
            int result = NodeTable.Top;

            for (int level = 1; level <= manager.VariableCount; level++)
            {
                result = table.GetNode(level, result, result);
            }

            return result;
        }

        private static ArgumentOutOfRangeException Unsupported(ZddOperation op) =>
            new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the family algebra operations (Product / Quotient / Remainder).");
    }
}

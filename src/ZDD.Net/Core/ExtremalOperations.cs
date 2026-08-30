using System;

namespace ZDD.Net.Core
{
    /// <summary>
    /// item を取らない単項演算（<see cref="ZddOperation.Maximal"/> / <see cref="ZddOperation.Minimal"/> /
    /// <see cref="ZddOperation.HittingSets"/> / <see cref="ZddOperation.Complement"/>）の実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>何のための演算か</b>: <see cref="ZddOperation.Maximal"/> / <see cref="ZddOperation.Minimal"/> は
    /// 「冗長な解を落とす」定番操作で、極小カットや極小頂点被覆のように
    /// <b>包含関係で下（上）にある解だけが要る</b>問題に効く。
    /// <see cref="ZddOperation.HittingSets"/> は超グラフの双対（横断超グラフ）を取る演算で、
    /// 故障解析や最小ヒッティング集合問題に使う。<see cref="ZddOperation.Complement"/> は族としての補
    /// <c>2^U ∖ F</c> で、ド・モルガン則が書けるようになる。
    /// </para>
    /// <para>
    /// <b>3 つの走査と 1 つの差</b>: 極大・極小は<b>自分のノードだけ</b>を辿る走査
    /// （<see cref="UnaryOperations"/> と同じくキーはノード ID 1 個）。ヒッティング集合は
    /// 「どのレベルまでを全体集合と見るか」が答を変えるので、キーが
    /// <c>(ノード, レベル)</c> の対になる。補は冪集合との差そのものなので、走査を書かずに
    /// <see cref="BinaryOperations"/> に任せる。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>。ZDD の深さは変数の個数そのもので、10 万規模の族を素直な再帰で辿ると
    /// <c>StackOverflowException</c> になり、.NET ではこれを catch できずプロセスが即死する
    /// （docs/PLAN.md §4.5）。入れ子で呼ぶ演算（ふるい・和・差）もすべて反復実装なので、
    /// この経路にネイティブスタックを深く食う箇所は無い（入れ子の深さは
    /// 「極小 → ふるい → 和／交わり」の 2 段までで、族の大きさに依らない）。
    /// </para>
    /// </remarks>
    internal static class ExtremalOperations
    {
        /// <summary>
        /// 族に item を取らない単項演算を適用し、結果の根ノード ID を返す。
        /// </summary>
        /// <param name="manager">族を所有するマネージャ。</param>
        /// <param name="op">
        /// <see cref="ZddOperation.Maximal"/> / <see cref="ZddOperation.Minimal"/> /
        /// <see cref="ZddOperation.HittingSets"/> / <see cref="ZddOperation.Complement"/> のいずれか。
        /// </param>
        /// <param name="rootId">入力の族の根ノード ID。</param>
        /// <returns>結果の族の根ノード ID。</returns>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static int Apply(ZddManager manager, ZddOperation op, int rootId) =>
            op switch
            {
                ZddOperation.Maximal or ZddOperation.Minimal => Extremal(manager, op, rootId),
                ZddOperation.HittingSets => HittingSets(manager, rootId),
                ZddOperation.Complement => Complement(manager, rootId),
                _ => throw Unsupported(op),
            };

        // ---- 極大・極小 ----

        /// <summary>
        /// 包含関係で極大（極小）な集合だけを残し、結果の根ノード ID を返す。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>分解の形</b>: レベル <c>v</c> で <c>f = f₀ ∪ v·f₁</c> と読む
        /// （<c>v·X</c> は「X の各集合に item を足したもの」）。<c>v</c> を含む集合と含まない集合の
        /// 包含関係は<b>片方向にしか起こらない</b>（<c>v</c> を含む集合が、含まない集合に
        /// 含まれることはない）ので、ふるいが要るのは常に片側だけになる:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <b>極小</b>: <c>v</c> を含まない側は <c>f₀</c> の中だけで決まる。<c>v</c> を含む側は
        /// <c>f₁</c> の極小元のうち、<c>f₀</c> の極小元を含んでしまうものを落とす
        /// （<c>b ⊆ a</c> なら <c>b ⊆ v ∪ a</c> なので、そちらのほうが小さい）。
        /// </description></item>
        /// <item><description>
        /// <b>極大</b>: 裏返しになり、<c>v</c> を含む側は <c>f₁</c> の中だけで決まる。
        /// <c>v</c> を含まない側は <c>f₀</c> の極大元のうち、<c>f₁</c> の極大元に含まれるものを落とす。
        /// </description></item>
        /// </list>
        /// <para>
        /// <b>相手を極大／極小に絞ってよい理由</b>: 「<c>f₀</c> のどれかを含む」ことと
        /// 「<c>f₀</c> の<b>極小元</b>のどれかを含む」ことは同値である（含んだ相手より小さい極小元が
        /// 必ず族の中にいる）。極大側も同様。すでに計算済みの答をそのまま相手にできるので、
        /// 元の子をもう一度持ち回らずに済む。
        /// </para>
        /// <para>
        /// ふるいは <see cref="ContainmentOperations"/> の
        /// <see cref="ZddOperation.NonSupersetsOf"/> / <see cref="ZddOperation.NonSubsetsOf"/> を
        /// そのまま呼ぶ。呼ばれた側は自前の作業領域を借りて回るので、こちらのスタックとは混ざらない。
        /// </para>
        /// </remarks>
        private static int Extremal(ZddManager manager, ZddOperation op, int rootId)
        {
            // 終端は自分自身が答（∅ には要素が無く、{∅} の唯一の要素は極大でも極小でもある）。
            if (NodeTable.IsTerminal(rootId))
            {
                return rootId;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            // 走査の途中で変わらないので、ループの外で 1 度だけ決める。
            bool keepsMinimal = op == ZddOperation.Minimal;

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
                        // ref はふるいや GetNode（ノード表を伸ばしうる）を挟むと古い配列を指しうるため、
                        // 必要な値をここで読み切ってしまう。
                        int level;
                        int nodeLo;
                        int nodeHi;
                        {
                            ref ZddNode node = ref nodes[id];
                            level = node.Level;
                            nodeLo = node.Lo;
                            nodeHi = node.Hi;
                        }

                        work.TryGetResult(nodeLo, out int lo);
                        work.TryGetResult(nodeHi, out int hi);

                        if (keepsMinimal)
                        {
                            // item を含む極小元から、item を含まない極小元の上位集合を落とす。
                            hi = Filter(manager, ZddOperation.NonSupersetsOf, hi, lo);
                        }
                        else
                        {
                            // item を含まない極大元から、item を含む極大元の部分集合を落とす。
                            lo = Filter(manager, ZddOperation.NonSubsetsOf, lo, hi);
                        }

                        // ゼロサプレス規則と一意化は GetNode が引き受ける。
                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(id, combined);
                        cache.PutUnary(op, id, 0, combined);
                        continue;
                    }

                    // 1) 途中結果表: 別の親が既に片付けていれば、それ以上何もしない。
                    if (work.HasResult(id))
                    {
                        continue;
                    }

                    // 2) 基底ケース: 終端はそれ自身が答。
                    if (NodeTable.IsTerminal(id))
                    {
                        work.SetResult(id, id);
                        continue;
                    }

                    // 3) 演算キャッシュ: 過去の演算で同じ部分問題を解いていれば、その答を使う。
                    if (cache.TryGetUnary(op, id, 0, out int cached))
                    {
                        work.SetResult(id, cached);
                        continue;
                    }

                    // 4) 1 段降りる。自分を先に積み、その上に未計算の子を積む。
                    int childLo;
                    int childHi;
                    {
                        ref ZddNode node = ref nodes[id];
                        childLo = node.Lo;
                        childHi = node.Hi;
                    }

                    work.PushCombine(id);

                    if (!work.HasResult(childLo))
                    {
                        work.PushVisit(childLo);
                    }

                    if (!work.HasResult(childHi))
                    {
                        work.PushVisit(childHi);
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

        // ---- ヒッティング集合 ----

        /// <summary>
        /// <c>{ a ⊆ U : ∀ b ∈ f, a ∩ b ≠ ∅ }</c>（ブロッキング集合族 / 横断超グラフ）を求める。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>全体集合はマネージャの変数すべて</b>（<see cref="ZddManager.VariableCount"/>）。
        /// <c>f</c> が一度も使っていない item も候補に自由に入れてよいので、<c>Support</c> ではなく
        /// 変数の個数が答を決める。
        /// </para>
        /// <para>
        /// <b>キーがノードだけでは足りない</b>: 「レベル <c>k</c> 以下の item から作れる候補」に
        /// 答が依るので、部分問題は <c>(ノード, レベル)</c> の対になる。同じノードでも、
        /// その上に自由な item が何段あるかで答の大きさが変わる。詰め方は二項演算のキーと同じで
        /// （<see cref="OperationKey"/>）、左にノード ID、右にレベルを置く。
        /// </para>
        /// <para>
        /// <b>分解の形</b>: レベル <c>v</c> で <c>f = f₀ ∪ v·f₁</c> と読むと、候補 <c>a</c> は
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <c>v ∉ a</c> のとき: <c>f₀</c> の各集合と交わり、かつ <c>v ∪ c</c>（<c>c ∈ f₁</c>）とも
        /// 交わらねばならないが、<c>v</c> は使えないので <c>c</c> と交わる必要がある。
        /// つまり <c>f₀ ∪ f₁</c>（＝ <c>f</c> の各集合から <c>v</c> を取り除いた族）のヒッティング集合。
        /// </description></item>
        /// <item><description>
        /// <c>v ∈ a</c> のとき: <c>v</c> を含む集合はそれだけで交わるので、残りは <c>f₀</c> の
        /// ヒッティング集合であればよい。
        /// </description></item>
        /// </list>
        /// <para>
        /// <c>f</c> より根側の item（<c>f</c> に現れない item）は、入れても入れなくても条件に
        /// 関わらない。そこだけ 0-枝と 1-枝が同じ答を指すノードになり、結果は自然に冪集合ぶんだけ
        /// 膨らむ。<c>f = ∅</c> の答が <c>2^U</c> になるのもこの規則の帰結である（条件が空虚に真）。
        /// </para>
        /// <para>
        /// <b>結果は指数的に大きくなりうる</b>。ヒッティング集合族はもとの族より遥かに大きな
        /// ZDD になることがあり（横断超グラフの大きさは入力に対して指数的になりうる）、
        /// 極小なものだけが要るなら <c>HittingSets().Minimal()</c> と書く。
        /// </para>
        /// </remarks>
        private static int HittingSets(ZddManager manager, int rootId)
        {
            // ∅ を要素に持つ族は、どの候補も ∅ とは交われないので答が空になる。
            // {∅} 自身がこの形なので、作業領域を借りる前に返せる。
            if (rootId == NodeTable.Top)
            {
                return NodeTable.Bottom;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(ZddOperation.HittingSets, rootId, manager.VariableCount);
                work.PushVisit(rootKey);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    int f = OperationKey.LeftOf(key);
                    int level = OperationKey.RightOf(key);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // 子は必ず計算済み（LIFO）。分解はノード表を読むだけなので、
                        // 積んだときと同じ部分問題が出る。
                        int lo;
                        int hi;

                        if (level > LevelOf(nodes, f))
                        {
                            // f に現れない item。入れても入れなくても条件に関わらないので、
                            // 両方の枝が同じ答を指す。
                            lo = HittingOf(work, f, level - 1);
                            hi = lo;
                        }
                        else
                        {
                            int f0;
                            int f1;
                            {
                                ref ZddNode node = ref nodes[f];
                                f0 = node.Lo;
                                f1 = node.Hi;
                            }

                            // item を採らないなら、f の各集合から item を取り除いた族を叩く必要がある。
                            // 和は積んだときと同じ値になる（同じ 2 つのノードの和なので）。
                            lo = HittingOf(work, Shadow(manager, f0, f1), level - 1);
                            hi = HittingOf(work, f0, level - 1);
                        }

                        // ゼロサプレス規則と一意化は GetNode が引き受ける。
                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(key, combined);
                        cache.PutUnary(ZddOperation.HittingSets, f, level, combined);
                        continue;
                    }

                    // 1) 途中結果表 → 2) 基底ケース → 3) 演算キャッシュ の順に見る。
                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    if (TryResolveHitting(f, level, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    if (cache.TryGetUnary(ZddOperation.HittingSets, f, level, out int cached))
                    {
                        work.SetResult(key, cached);
                        continue;
                    }

                    // 4) 1 段降りる。自分を先に積み、その上に未計算の子を積む。
                    work.PushCombine(key);

                    if (level > LevelOf(nodes, f))
                    {
                        PushHitting(work, f, level - 1);
                    }
                    else
                    {
                        int childLo;
                        int childHi;
                        {
                            ref ZddNode node = ref nodes[f];
                            childLo = node.Lo;
                            childHi = node.Hi;
                        }

                        PushHitting(work, Shadow(manager, childLo, childHi), level - 1);
                        PushHitting(work, childLo, level - 1);
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
        /// 終端が絡むヒッティング集合の答を返す。
        /// </summary>
        /// <returns>答が決まれば <see langword="true"/>。</returns>
        /// <remarks>
        /// <c>{∅}</c> はどの候補とも交われないので答が空。変数を使い切った（レベル 0 の）
        /// <c>∅</c> は「条件が 1 つも無い」ので、唯一の候補である ∅ が残る。
        /// レベルが残っている <c>∅</c> はここでは決めず、自由な item を 1 段ずつ積んで冪集合を作る。
        /// </remarks>
        private static bool TryResolveHitting(int f, int level, out int result)
        {
            if (f == NodeTable.Top)
            {
                result = NodeTable.Bottom;
                return true;
            }

            if (f == NodeTable.Bottom && level == 0)
            {
                result = NodeTable.Top;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>部分問題 <c>(f, level)</c> を積む。その場で答が決まる対は表にも積まない。</summary>
        /// <remarks>合成側と<b>同じ条件で同じキー</b>を作ること。</remarks>
        private static void PushHitting(OperationWorkspace work, int f, int level)
        {
            if (TryResolveHitting(f, level, out _))
            {
                return;
            }

            long key = HittingKey(f, level);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>計算済みの部分問題 <c>(f, level)</c> の答。<see cref="PushHitting"/> と対になっている。</summary>
        private static int HittingOf(OperationWorkspace work, int f, int level)
        {
            if (TryResolveHitting(f, level, out int direct))
            {
                return direct;
            }

            work.TryGetResult(HittingKey(f, level), out int result);
            return result;
        }

        /// <summary>部分問題 <c>(f, level)</c> のキー。</summary>
        private static long HittingKey(int f, int level) =>
            OperationKey.Of(ZddOperation.HittingSets, f, level);

        /// <summary>
        /// <c>f</c> の各集合から分岐 item を取り除いた族（<c>f₀ ∪ f₁</c>）。
        /// </summary>
        /// <remarks>
        /// 呼ばれた和は自分の作業領域を借りるので、こちらのスタックには影響しない。
        /// 積むときと合成するときの 2 回呼ぶことになるが、和は同じ 2 つのノードから同じ答を返すので
        /// 食い違わない（2 回目は演算キャッシュに当たる）。
        /// </remarks>
        private static int Shadow(ZddManager manager, int f0, int f1) =>
            BinaryOperations.Apply(manager, ZddOperation.Union, f0, f1);

        // ---- 補 ----

        /// <summary>
        /// 補 <c>2^U ∖ f</c> を求める。全体集合 <c>U</c> はマネージャの全変数。
        /// </summary>
        /// <remarks>
        /// 冪集合との差そのものなので、専用の走査は書かない
        /// （<see cref="BinaryOperations"/> の差は反復実装で、冪集合は変数の個数ぶんのノードしか持たない）。
        /// 結果を単項演算としてもキャッシュに載せておくと、同じ族の補を繰り返し取るときに
        /// 冪集合の組み立てごと省ける。
        /// </remarks>
        private static int Complement(ZddManager manager, int rootId)
        {
            OperationCache cache = manager.Cache;

            if (cache.TryGetUnary(ZddOperation.Complement, rootId, 0, out int cached))
            {
                return cached;
            }

            int result = BinaryOperations.Apply(
                manager,
                ZddOperation.Difference,
                manager.PowerSetRoot(),
                rootId);

            cache.PutUnary(ZddOperation.Complement, rootId, 0, result);
            return result;
        }

        // ---- 共通の道具 ----

        /// <summary>ノードのレベル。終端は 0（どの item よりも葉側）。</summary>
        private static int LevelOf(NodeTable nodes, int id) =>
            NodeTable.IsTerminal(id) ? 0 : nodes[id].Level;

        /// <summary>
        /// 極大・極小の合成で使うふるいを 1 回かける。呼ばれた側は自分の作業領域を借りるので、
        /// こちらのスタックには影響しない。
        /// </summary>
        private static int Filter(ZddManager manager, ZddOperation op, int f, int g) =>
            ContainmentOperations.Apply(manager, op, f, g);

        private static ArgumentOutOfRangeException Unsupported(ZddOperation op) =>
            new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the item-less unary operations " +
                "(Maximal / Minimal / HittingSets / Complement).");
    }
}

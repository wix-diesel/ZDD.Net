using System;

namespace ZDD.Net.Core
{
    /// <summary>
    /// 包含関係で族をふるいにかける演算（<see cref="ZddOperation.Meet"/> /
    /// <see cref="ZddOperation.SupersetsOf"/> / <see cref="ZddOperation.SubsetsOf"/> /
    /// <see cref="ZddOperation.NonSubsetsOf"/> / <see cref="ZddOperation.NonSupersetsOf"/>）の実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>何のための演算か</b>: 構築済みの巨大な族を<b>後から絞り込む</b>のが主な用途である
    /// （「全域木のうち、この辺集合を含むもの」「パスのうち、この辺を通らないもの」）。
    /// 族を作り直すのではなく、できあがった ZDD をそのまま辿ってふるいにかけるので、
    /// 10^20 個の集合を持つ族でもノード数に比例した手間で済む。
    /// </para>
    /// <para>
    /// <b>4 つのふるいは 1 つの走査で書ける</b>。どれも「候補 <c>a ∈ f</c> に対し、
    /// <c>g</c> の中に決まった向きの包含関係を満たす <c>b</c> がいるか」を見る演算で、違うのは 2 点だけ:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>探す向き</b>（<see cref="SeeksSubsetsInG"/>）: <c>b ⊆ a</c> を探すのが
    /// <see cref="ZddOperation.SupersetsOf"/>（Restrict）と <see cref="ZddOperation.NonSupersetsOf"/>、
    /// <c>a ⊆ b</c> を探すのが <see cref="ZddOperation.SubsetsOf"/>（Permit）と
    /// <see cref="ZddOperation.NonSubsetsOf"/>。
    /// </description></item>
    /// <item><description>
    /// <b>見つかった候補を残すか捨てるか</b>（<see cref="KeepsMatches"/>）: 残すのが Restrict / Permit、
    /// 捨てるのがその否定版 2 つ。
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>分解の形</b>: レベル <c>v</c> で <c>f = f₀ ∪ v·f₁</c>、<c>g = g₀ ∪ v·g₁</c> と読む
    /// （<c>v·X</c> は「X の各集合に item を足したもの」）。<c>b ⊆ a</c> を探す側では、
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>a ∈ f₀</c>（<c>v</c> を含まない）の相手は <c>v</c> を含めないので <c>g₀</c> だけ。
    /// </description></item>
    /// <item><description>
    /// <c>a = v ∪ a'</c> の相手は <c>g₀</c> にも <c>g₁</c> にもいるので、<b>1-枝で 2 つの答が合流する</b>。
    /// </description></item>
    /// </list>
    /// <para>
    /// <c>a ⊆ b</c> を探す側はこれがちょうど裏返り、合流するのは 0-枝になる。合流のさせ方は
    /// 「見つけたものを残す」なら和、「見つけたものを捨てる」なら交わり
    /// （<c>∃</c> の合併が和、<c>∀</c> の重ね合わせが交わりに当たる）。
    /// つまり合成のたびに<b>別の演算</b>（<see cref="ZddOperation.Union"/> /
    /// <see cref="ZddOperation.Intersect"/>）を高々 1 回呼ぶ。呼ばれた側は自前の作業領域を借りて回るので
    /// （<see cref="ZddManager.RentWorkspace"/> は入れ子の深さぶんを使い回す）、こちらのスタックと混ざらない。
    /// </para>
    /// <para>
    /// <b><see cref="ZddOperation.Meet"/> だけは形が違う</b>。ふるいではなく
    /// <c>{ a ∩ b : a ∈ f, b ∈ g }</c> を<b>作る</b>演算なので、<see cref="FamilyAlgebraOperations"/> の
    /// 積と同じく 4 つの部分問題を持つ。<c>a ∩ b</c> が <c>v</c> を含むのは両方が含むときだけなので、
    /// 積とは逆に<b>0-枝に 3 項が集まる</b>。
    /// </para>
    /// <para>
    /// <b>再帰は書かない</b>。ZDD の深さは変数の個数そのもので、10 万規模の族を素直な再帰で辿ると
    /// <c>StackOverflowException</c> になり、.NET ではこれを catch できずプロセスが即死する
    /// （docs/PLAN.md §4.5）。入れ子で呼ぶ集合演算も反復実装なので、この経路にネイティブスタックを
    /// 深く食う箇所は無い（入れ子の深さは「ふるい → 和／交わり」の 1 段だけで、族の大きさに依らない）。
    /// </para>
    /// </remarks>
    internal static class ContainmentOperations
    {
        /// <summary>
        /// 2 つの族に包含系の演算を適用し、結果の根ノード ID を返す。
        /// </summary>
        /// <param name="manager">両方の族を所有するマネージャ。</param>
        /// <param name="op">
        /// <see cref="ZddOperation.Meet"/> / <see cref="ZddOperation.SupersetsOf"/> /
        /// <see cref="ZddOperation.SubsetsOf"/> / <see cref="ZddOperation.NonSubsetsOf"/> /
        /// <see cref="ZddOperation.NonSupersetsOf"/> のいずれか。
        /// </param>
        /// <param name="fRoot">ふるいにかけられる側（左オペランド）の根ノード ID。</param>
        /// <param name="gRoot">相手（右オペランド）の根ノード ID。</param>
        /// <returns>結果の族の根ノード ID。</returns>
        /// <exception cref="ObjectDisposedException">
        /// <paramref name="manager"/> が破棄済みの場合。
        /// </exception>
        public static int Apply(ZddManager manager, ZddOperation op, int fRoot, int gRoot) =>
            op switch
            {
                ZddOperation.Meet => Meet(manager, fRoot, gRoot),
                ZddOperation.SupersetsOf
                    or ZddOperation.SubsetsOf
                    or ZddOperation.NonSubsetsOf
                    or ZddOperation.NonSupersetsOf => Filter(manager, op, fRoot, gRoot),
                _ => throw Unsupported(op),
            };

        // ---- ふるい（Restrict / Permit とその否定版）----

        /// <summary>
        /// <paramref name="op"/> の向きに従って <c>f</c> の要素をふるいにかけ、結果の根ノード ID を返す。
        /// </summary>
        /// <remarks>
        /// 結果は必ず <c>f</c> の<b>部分族</b>になる（集合そのものは作り替えない）。
        /// <see cref="MergeSides"/> の近道はこの性質に寄りかかっている。
        /// </remarks>
        private static int Filter(ZddManager manager, ZddOperation op, int fRoot, int gRoot)
        {
            // 終端が絡む組合せはここで片付く。作業領域を借りる前に返せるので、
            // 単発の f.Restrict(Base) のような呼び出しは表に触れない。
            if (TryResolveFilter(op, fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            // 走査の途中で変わらない性質なので、ループの外で 1 度だけ決める。
            bool seeksSubsets = SeeksSubsetsInG(op);

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
                        NodePair.Split(nodes, f, g, out int level, out int f0, out int f1, out int g0, out int g1);

                        int lo;
                        int hi;

                        if (seeksSubsets)
                        {
                            // b ⊆ a を探す。item を含まない候補の相手は g₀ に限られ、
                            // item を含む候補だけが g の両側と突き合わせになる。
                            lo = FilterOf(work, op, f0, g0);
                            hi = MergeSides(manager, work, op, f1, g0, g1);
                        }
                        else
                        {
                            // a ⊆ b を探す。item を含む候補の相手は item を含む集合に限られるので、
                            // 合流するのは item を含まない側になる。
                            lo = MergeSides(manager, work, op, f0, g0, g1);
                            hi = FilterOf(work, op, f1, g1);
                        }

                        // ゼロサプレス規則と一意化は GetNode が引き受ける。
                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(key, combined);
                        cache.PutBinary(op, f, g, combined);
                        continue;
                    }

                    // 1) 途中結果表: 別の親が既に片付けていれば、それ以上何もしない。
                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    // 2) 基底ケース: 終端や同じ族どうしは、降りずにその場で答が決まる。
                    if (TryResolveFilter(op, f, g, out int direct))
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
                    NodePair.Split(
                        nodes,
                        f,
                        g,
                        out _,
                        out int childF0,
                        out int childF1,
                        out int childG0,
                        out int childG1);

                    work.PushCombine(key);

                    if (seeksSubsets)
                    {
                        PushFilter(work, op, childF0, childG0);
                        PushFilter(work, op, childF1, childG0);
                        PushFilter(work, op, childF1, childG1);
                    }
                    else
                    {
                        PushFilter(work, op, childF0, childG0);
                        PushFilter(work, op, childF0, childG1);
                        PushFilter(work, op, childF1, childG1);
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
        /// 合流する側の枝の答。<c>f</c> を <c>g</c> の両側それぞれでふるいにかけ、2 つの答を合成する。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 合成は「見つけたものを残す」なら和、「捨てる」なら交わり（<see cref="MergeOperationOf"/>）。
        /// </para>
        /// <para>
        /// <b>片側が ∅ なら合成そのものが要らない</b>。<c>g</c> の片側に集合が 1 つも無ければ、
        /// その側のふるいは残す演算なら ∅（単位元）を、捨てる演算なら <c>f</c> をそのまま返す。
        /// 後者が単位元になるのは、ふるいの結果が必ず <c>f</c> の部分族だからである
        /// （<c>x ∩ f = x</c>）。<see cref="NodePair.Split"/> は「片方だけが上にある」対で
        /// 必ず <c>g₁ = ∅</c> を返すので、この近道は珍しい場合の手当てではなく<b>常道</b>にあたる。
        /// </para>
        /// </remarks>
        private static int MergeSides(
            ZddManager manager,
            OperationWorkspace work,
            ZddOperation op,
            int f,
            int g0,
            int g1)
        {
            if (g1 == NodeTable.Bottom)
            {
                return FilterOf(work, op, f, g0);
            }

            if (g0 == NodeTable.Bottom)
            {
                return FilterOf(work, op, f, g1);
            }

            return BinaryOperations.Apply(
                manager,
                MergeOperationOf(op),
                FilterOf(work, op, f, g0),
                FilterOf(work, op, f, g1));
        }

        /// <summary>
        /// 終端や同じ族どうしの組合せの答を返す。
        /// </summary>
        /// <returns>答が決まれば <see langword="true"/>。</returns>
        /// <remarks>
        /// <para>
        /// 4 演算に共通するのは次の 3 つ。<c>f</c> が ∅ ならふるいにかける候補が無い。
        /// <c>g</c> が ∅ なら「∃ b」は偽、「∀ b」は空虚に真。<c>f == g</c> なら
        /// <c>a ⊆ a</c> も <c>a ⊇ a</c> も成り立つので、どの候補も必ず相手を見つける。
        /// </para>
        /// <para>
        /// 残りは <c>{∅}</c>（<see cref="ZddManager.Base"/>）が絡む近道で、<b>探す向きの側にだけ</b>効く。
        /// <c>b ⊆ a</c> を探すとき <c>g == {∅}</c> なら、∅ はどの候補にも含まれるので全員が一致する。
        /// <c>a ⊆ b</c> を探すとき <c>f == {∅}</c> なら、候補の ∅ はどの相手にも含まれるので同じく一致する。
        /// 裏返した組合せ（たとえば <c>Restrict({∅}, g)</c>）は「g が ∅ を持つか」に答が依るので、
        /// 定数時間では決まらない。<c>g</c> の 0-枝を辿れば分かるので、そのまま分解に任せる。
        /// </para>
        /// </remarks>
        private static bool TryResolveFilter(ZddOperation op, int f, int g, out int result)
        {
            bool keepsMatches = KeepsMatches(op);

            if (f == NodeTable.Bottom)
            {
                // ふるいにかける候補が 1 つも無い。
                result = NodeTable.Bottom;
                return true;
            }

            if (g == NodeTable.Bottom)
            {
                result = keepsMatches ? NodeTable.Bottom : f;
                return true;
            }

            if (f == g)
            {
                result = keepsMatches ? f : NodeTable.Bottom;
                return true;
            }

            if (SeeksSubsetsInG(op) ? g == NodeTable.Top : f == NodeTable.Top)
            {
                // 部分集合を探す側なら相手が ∅、上位集合を探す側なら候補が ∅ だけ。
                // どちらも包含関係が必ず成り立つので、候補は全員が一致する。
                result = keepsMatches ? f : NodeTable.Bottom;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>部分問題 <c>(f, g)</c> を積む。その場で答が決まる対は表にも積まない。</summary>
        /// <remarks>
        /// <see cref="FilterOf"/> と対になっていて、<b>同じ条件で同じキーを作る</b>こと。
        /// 積むときと読むときで判定がずれると、合成が別の部分問題の答を拾う。
        /// </remarks>
        private static void PushFilter(OperationWorkspace work, ZddOperation op, int f, int g)
        {
            if (TryResolveFilter(op, f, g, out _))
            {
                return;
            }

            long key = OperationKey.Of(op, f, g);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>計算済みの部分問題 <c>(f, g)</c> の答。<see cref="PushFilter"/> と対になっている。</summary>
        private static int FilterOf(OperationWorkspace work, ZddOperation op, int f, int g)
        {
            if (TryResolveFilter(op, f, g, out int direct))
            {
                return direct;
            }

            work.TryGetResult(OperationKey.Of(op, f, g), out int result);
            return result;
        }

        /// <summary>
        /// <c>g</c> の中から候補の<b>部分集合</b>を探す演算かどうか
        /// （<see cref="ZddOperation.SupersetsOf"/> = Restrict と <see cref="ZddOperation.NonSupersetsOf"/>）。
        /// 偽なら候補の<b>上位集合</b>を探す（<see cref="ZddOperation.SubsetsOf"/> = Permit と
        /// <see cref="ZddOperation.NonSubsetsOf"/>）。
        /// </summary>
        /// <remarks>
        /// 「2 つの答が合流するのが 1-枝か 0-枝か」もこの値で決まる。<c>b ⊆ a</c> を探すなら
        /// item を含む候補だけが相手を 2 通り持ちうるので 1-枝、<c>a ⊆ b</c> を探すならその逆。
        /// </remarks>
        private static bool SeeksSubsetsInG(ZddOperation op) =>
            op is ZddOperation.SupersetsOf or ZddOperation.NonSupersetsOf;

        /// <summary>
        /// 相手が見つかった候補を<b>残す</b>演算かどうか（Restrict / Permit）。
        /// 偽なら見つかった候補を捨てる（否定版 2 つ）。
        /// </summary>
        private static bool KeepsMatches(ZddOperation op) =>
            op is ZddOperation.SupersetsOf or ZddOperation.SubsetsOf;

        /// <summary>
        /// 合流する枝で 2 つの答を合成する演算。<c>∃</c> を集めるのが和、<c>∀</c> を重ねるのが交わり。
        /// </summary>
        private static ZddOperation MergeOperationOf(ZddOperation op) =>
            KeepsMatches(op) ? ZddOperation.Union : ZddOperation.Intersect;

        // ---- Meet ----

        /// <summary>
        /// <c>f ⊓ g = { a ∩ b : a ∈ f, b ∈ g }</c> を求める。
        /// </summary>
        /// <remarks>
        /// ふるいと違って結果は <c>f</c> の部分族とは限らない（新しい集合が現れる）。
        /// 走査の形は <see cref="FamilyAlgebraOperations"/> の積と同じで、
        /// 3 項が集まるのが 1-枝ではなく 0-枝である点だけが違う。
        /// </remarks>
        private static int Meet(ZddManager manager, int fRoot, int gRoot)
        {
            if (TryResolveMeet(fRoot, gRoot, out int trivial))
            {
                return trivial;
            }

            UniqueTable table = manager.Table;
            OperationCache cache = manager.Cache;
            NodeTable nodes = table.Nodes;

            OperationWorkspace work = manager.RentWorkspace();
            try
            {
                long rootKey = OperationKey.Of(ZddOperation.Meet, fRoot, gRoot);
                work.PushVisit(rootKey);

                while (work.TryPop(out long entry))
                {
                    long key = OperationWorkspace.KeyOf(entry);
                    int f = OperationKey.LeftOf(key);
                    int g = OperationKey.RightOf(key);

                    if (OperationWorkspace.IsCombine(entry))
                    {
                        // 子は必ず計算済み（LIFO）。分解はノード表を読むだけなので、積んだときと同じ形になる。
                        NodePair.Split(nodes, f, g, out int level, out int f0, out int f1, out int g0, out int g1);

                        // item が交わりに残るのは、両方が item を含むときだけ。
                        int hi = MeetOf(work, f1, g1);

                        // 残り 3 通りの組合せは、どれも item を含まない交わりを作る。
                        // ∅ が混ざる呼び出しは Union 側の終端処理が作業領域を借りずに返すので、
                        // ここで場合分けはしない。
                        int lo = MeetOf(work, f0, g0);
                        lo = Combine(manager, lo, MeetOf(work, f0, g1));
                        lo = Combine(manager, lo, MeetOf(work, f1, g0));

                        // ゼロサプレス規則と一意化は GetNode が引き受ける。
                        int combined = table.GetNode(level, lo, hi);

                        work.SetResult(key, combined);
                        cache.PutBinary(ZddOperation.Meet, f, g, combined);
                        continue;
                    }

                    // 1) 途中結果表 → 2) 基底ケース → 3) 演算キャッシュ の順に見る。
                    if (work.HasResult(key))
                    {
                        continue;
                    }

                    if (TryResolveMeet(f, g, out int direct))
                    {
                        work.SetResult(key, direct);
                        continue;
                    }

                    if (cache.TryGetBinary(ZddOperation.Meet, f, g, out int cached))
                    {
                        work.SetResult(key, cached);
                        continue;
                    }

                    // 4) 1 段降りる。自分を先に積み、その上に未計算の子を積む。
                    NodePair.Split(
                        nodes,
                        f,
                        g,
                        out _,
                        out int childF0,
                        out int childF1,
                        out int childG0,
                        out int childG1);

                    work.PushCombine(key);
                    PushMeet(work, childF0, childG0);
                    PushMeet(work, childF0, childG1);
                    PushMeet(work, childF1, childG0);
                    PushMeet(work, childF1, childG1);
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
        /// 終端が絡む Meet の答を返す。<c>∅ ⊓ g = ∅</c>、<c>{∅} ⊓ g = {∅}</c> の 2 つで尽きる。
        /// </summary>
        /// <returns>答が決まれば <see langword="true"/>。</returns>
        /// <remarks>
        /// <c>f == g</c> は近道にならない。<c>f ⊓ f</c> は <c>f</c> を含むが、
        /// 要素どうしの交わりが新しく増える（<c>{{0}, {1}} ⊓ {{0}, {1}} = {∅, {0}, {1}}</c>）。
        /// </remarks>
        private static bool TryResolveMeet(int f, int g, out int result)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                // 相手が 1 つも集合を持たないので、作れる交わりも 1 つも無い。
                result = NodeTable.Bottom;
                return true;
            }

            if (f == NodeTable.Top || g == NodeTable.Top)
            {
                // ∅ との交わりは常に ∅。相手が何個集合を持っていても、できる族は {∅} 1 通り。
                result = NodeTable.Top;
                return true;
            }

            result = NodeTable.Bottom;
            return false;
        }

        /// <summary>部分問題 <c>(f, g)</c> を積む。∅ が絡む対はその場で答が決まるので、表にも積まない。</summary>
        /// <remarks><see cref="MeetOf"/> と対になっていて、同じ条件で同じキーを作ること。</remarks>
        private static void PushMeet(OperationWorkspace work, int f, int g)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                return;
            }

            long key = OperationKey.Of(ZddOperation.Meet, f, g);
            if (!work.HasResult(key))
            {
                work.PushVisit(key);
            }
        }

        /// <summary>計算済みの部分問題 <c>(f, g)</c> の答。<see cref="PushMeet"/> と対になっている。</summary>
        private static int MeetOf(OperationWorkspace work, int f, int g)
        {
            if (f == NodeTable.Bottom || g == NodeTable.Bottom)
            {
                return NodeTable.Bottom;
            }

            work.TryGetResult(OperationKey.Of(ZddOperation.Meet, f, g), out int result);
            return result;
        }

        /// <summary>
        /// 合成の途中で和を 1 回かける。呼ばれた側は自分の作業領域を借りるので、
        /// こちらのスタックには影響しない。
        /// </summary>
        private static int Combine(ZddManager manager, int f, int g) =>
            BinaryOperations.Apply(manager, ZddOperation.Union, f, g);

        private static ArgumentOutOfRangeException Unsupported(ZddOperation op) =>
            new ArgumentOutOfRangeException(
                nameof(op),
                $"'{op}' is not one of the containment operations " +
                "(Meet / SupersetsOf / SubsetsOf / NonSubsetsOf / NonSupersetsOf).");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Stress
{
    /// <summary>
    /// 変数 10 万の深い ZDD に対して、公開されている演算が<b>ひとつ残らず</b>完走することの回帰テスト。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>これが守っているもの</b>: ZDD の深さは変数の個数そのものなので、演算を素直な再帰で書くと
    /// 10 万段で <c>StackOverflowException</c> になる。.NET ではこの例外を catch できず<b>プロセスが即死する</b>
    /// ため、テストが「失敗」ではなく「テストランナーごと消える」形で壊れる。docs/PLAN.md §13 が
    /// 「影響: 大」に挙げているリスクで、対策は「設計初期から全演算を反復実装する」（§4.5）ことと、
    /// <b>それを回帰テストで固定すること</b>（§11-8）の 2 つである。ここが後者にあたる。
    /// </para>
    /// <para>
    /// <b>個々の演算のテストとの違いは網羅性</b>。各演算のテストにも深い族の項目があるが、
    /// それは「その演算を書いたときに落ちないことを確かめた」記録である。新しい演算が
    /// 反復で書かれていないまま入るのを止めるには、<b>公開 API を一望する場所</b>が要る。
    /// ここに追加を忘れた演算があれば、それは 10 万段で試されていない演算である。
    /// </para>
    /// <para>
    /// <b>お題の選び方</b>: 深さだけが狙いなので、結果が小さく収まる組み合わせを選ぶ。
    /// たとえば <see cref="Zdd.CountBySize"/> はノードごとに配列を作るので、1 本鎖に掛けると
    /// 段の数の二乗になる（<see cref="DeepZdd.Singletons"/> なら各ノードの配列が長さ 2 で収まる）。
    /// 同様に <see cref="Zdd.Count"/> を 2^100000 に掛けると 10 万ビットの
    /// <see cref="BigInteger"/> を 10 万回足すことになるので、そちらは <see cref="Zdd.CountApprox"/> で見る。
    /// 深さの検証としてはどちらも等価で、実行時間だけが違う。
    /// </para>
    /// <para>
    /// <b>一度外してあったお題</b>: <c>Singletons.IsSubsetOf(PowerSet)</c> は、かつて深さの二乗になった
    /// （変数 10 万で 10 秒以上）。<c>f</c> の 1-枝が ⊤ に着くたびに「<c>g</c> は空集合を持つか」を
    /// 0-枝の連なりを端まで辿って確かめ直していたためで、CI の予算を 1 つの呼び出しに
    /// 使い切らないよう <see cref="EnumerationAndMembershipFinish"/> から外してあった。
    /// #90 で <c>QueryOperations</c> がこの答を覚えるようになり線形になったので、お題を戻してある。
    /// 増え方そのものの回帰テストは <see cref="QueryScalingTests"/> にある。
    /// </para>
    /// </remarks>
    [Trait("Category", "Slow")]
    public class DeepZddStressTests : IClassFixture<DeepZdd>
    {
        private readonly DeepZdd _deep;

        public DeepZddStressTests(DeepZdd deep)
        {
            _deep = deep;
        }

        [Fact]
        public void TheFixtureBuildsThreeDiagramsOfFullDepth()
        {
            Assert.Equal((long)DeepZdd.VariableCount, _deep.Full.NodeCount);
            Assert.Equal((long)DeepZdd.VariableCount, _deep.Singletons.NodeCount);
            Assert.Equal((long)DeepZdd.VariableCount, _deep.PowerSet.NodeCount);

            // Support は 10 万段すべてを 1 度ずつ見る。
            Assert.Equal(DeepZdd.VariableCount, _deep.Full.Support().Length);
        }

        // ---- 単項演算 ----

        [Fact]
        public void UnaryOperationsFinish()
        {
            Zdd full = _deep.Full;

            // Change は対合。10 万段を 2 度降りて元に戻る。
            Assert.Equal(full, full.Change(0).Change(0));

            // {{0…99999}} から item 0 を取り出すと {{1…99999}}、除くと何も残らない。
            Assert.Equal(DeepZdd.VariableCount - 1, full.OnSet(0).Support().Length);
            Assert.True(full.OffSet(0).IsEmpty);

            // Flip は Change を順に掛けたもの。掛ける item の個数だけ 10 万段を降りる。
            Assert.Equal(full.Change(0).Change(1).Change(2), full.Flip(0, 1, 2));
        }

        [Fact]
        public void ExtremalOperationsFinish()
        {
            Zdd singletons = _deep.Singletons;

            // 1 要素集合どうしは包含関係にないので、極大も極小も自分自身。
            Assert.Equal(singletons, singletons.Maximal());
            Assert.Equal(singletons, singletons.Minimal());

            // どの {i} とも交わる集合は「全部入り」だけ。
            Assert.Equal(_deep.Full, singletons.HittingSets());

            // 補は 2 度取れば戻る。
            Assert.Equal(_deep.PowerSet, _deep.Full.Complement() | _deep.Full);
            Assert.Equal(_deep.Full, _deep.Full.Complement().Complement());
        }

        // ---- 二項演算 ----

        [Fact]
        public void SetOperationsFinish()
        {
            Zdd full = _deep.Full;
            Zdd singletons = _deep.Singletons;

            Zdd union = full | singletons;

            Assert.Equal(new BigInteger(DeepZdd.VariableCount + 1), union.Count);
            Assert.True((full & singletons).IsEmpty);
            Assert.Equal(full, full - singletons);
            Assert.Equal(union, full ^ singletons);

            // 冪集合はどちらも呑み込む。
            Assert.Equal(full, _deep.PowerSet & full);
            Assert.Equal(singletons, _deep.PowerSet & singletons);
        }

        [Fact]
        public void FamilyAlgebraOperationsFinish()
        {
            Zdd full = _deep.Full;

            // {{0…99999}} / {{0}} = {{1…99999}}、余りは無い。
            Zdd tail = full / _deep.Manager.Singleton(0);

            Assert.Equal(DeepZdd.VariableCount - 1, tail.Support().Length);
            Assert.True((full % _deep.Manager.Singleton(0)).IsEmpty);

            // 掛け戻せば元に戻る。
            Assert.Equal(full, tail * _deep.Manager.Singleton(0));

            // 深い族どうしの積・商・剰余。f / f = {∅}、f % f = ∅。
            Assert.Equal(_deep.Manager.Base, full / full);
            Assert.True((full % full).IsEmpty);
            Assert.Equal(full, full * _deep.Manager.Base);
        }

        [Fact]
        public void ContainmentOperationsFinish()
        {
            Zdd full = _deep.Full;
            Zdd singletons = _deep.Singletons;

            // 「全部入り」はどの {i} も含むので残り、逆向きには 1 つも残らない。
            Assert.Equal(full, full.SupersetsOf(singletons));
            Assert.True(full.SubsetsOf(singletons).IsEmpty);
            Assert.True(full.NonSupersetsOf(singletons).IsEmpty);
            Assert.Equal(full, full.NonSubsetsOf(singletons));

            // {{0…99999}} ⊓ {{i}} = {{i}}。交わりを集めると 1 要素集合が全部出てくる。
            Assert.Equal(singletons, full.Meet(singletons));
        }

        // ---- 評価 ----

        [Fact]
        public void EvaluationsFinish()
        {
            Assert.Equal(BigInteger.One, _deep.Full.Count);
            Assert.Equal(1.0, _deep.Full.CountApprox);

            Assert.Equal(new BigInteger(DeepZdd.VariableCount), _deep.Singletons.Count);

            // 2^100000 は BigInteger では高くつくので、規模だけ見る（double は 2^1024 で溢れる）。
            Assert.Equal(double.PositiveInfinity, _deep.PowerSet.CountApprox);

            // サイズ別の内訳。1 要素集合が 10 万個、それ以外は無い。
            BigInteger[] bySize = _deep.Singletons.CountBySize();

            Assert.Equal(2, bySize.Length);
            Assert.Equal(BigInteger.Zero, bySize[0]);
            Assert.Equal(new BigInteger(DeepZdd.VariableCount), bySize[1]);
        }

        // ---- 列挙とメンバシップ ----

        [Fact]
        public void EnumerationAndMembershipFinish()
        {
            int[] allItems = DeepZdd.AllItems();

            int[][] sets = _deep.Full.ToArray();

            Assert.Single(sets);
            Assert.Equal(allItems, sets[0]);

            // 冪集合は列挙し切れないが、先頭だけなら深さぶん降りて取れる。
            Assert.Empty(_deep.PowerSet.First());

            // 既定の順序は 0-枝を先に辿るので、いちばん葉に近い item の集合が先頭に来る。
            Assert.Equal(new[] { DeepZdd.VariableCount - 1 }, _deep.Singletons.First());

            Assert.True(_deep.Full.Contains(allItems));
            Assert.False(_deep.Singletons.Contains(allItems));

            Assert.True(_deep.Full.IsSubsetOf(_deep.PowerSet));
            Assert.False(_deep.PowerSet.IsSubsetOf(_deep.Full));

            // 1 要素集合の族を冪集合と突き合わせる形（#90 で線形になったお題）。
            // 打ち切りが効かないので、10 万段の対をひとつ残らず見る。
            Assert.True(_deep.Singletons.IsSubsetOf(_deep.PowerSet));
            Assert.False(_deep.PowerSet.IsSubsetOf(_deep.Singletons));

            Assert.True(_deep.Full.Overlaps(_deep.PowerSet));
            Assert.True(_deep.Singletons.Overlaps(_deep.PowerSet));
            Assert.False(_deep.Full.Overlaps(_deep.Singletons));
        }

        // ---- ランキングとサンプリング ----

        [Fact]
        public void RankingAndSamplingFinish()
        {
            int[] allItems = DeepZdd.AllItems();

            Assert.Equal(allItems, _deep.Full.ElementAt(0));
            Assert.Equal(BigInteger.Zero, _deep.Full.IndexOf(allItems));

            // 0-枝を先に辿るので、いちばん最後の item だけを持つ集合が先頭に来る。
            Assert.Equal(new[] { DeepZdd.VariableCount - 1 }, _deep.Singletons.ElementAt(0));
            Assert.Equal(new[] { 0 }, _deep.Singletons.ElementAt(DeepZdd.VariableCount - 1));
            Assert.Equal(
                new BigInteger(DeepZdd.VariableCount - 1),
                _deep.Singletons.IndexOf(new[] { 0 }));

            Random random = new Random(20260830);

            Assert.Equal(allItems, _deep.Full.Sample(random));

            foreach (int[] sample in _deep.Singletons.Sample(3, random))
            {
                Assert.Single(sample);
            }
        }

        // ---- 重み最適化 ----

        [Fact]
        public void WeightOperationsFinish()
        {
            int[] weights = new int[DeepZdd.VariableCount];
            double[] probabilities = new double[DeepZdd.VariableCount];
            double[] doubleWeights = new double[DeepZdd.VariableCount];

            for (int item = 0; item < DeepZdd.VariableCount; item++)
            {
                weights[item] = 1;
                probabilities[item] = 0.5;
                doubleWeights[item] = 1.0;
            }

            // 集合が 1 つしかないので、最大も最小も同じ「全部入り」。
            Assert.Equal(DeepZdd.VariableCount, _deep.Full.MaxWeight(weights).Weight);
            Assert.Equal(DeepZdd.VariableCount, _deep.Full.MinWeight(weights).Weight);
            Assert.Equal(DeepZdd.VariableCount, _deep.Full.TopK(weights, 3).Single().Weight);

            // 1 要素集合はどれも重み 1。上位 3 個を取っても、順位に応じた集合が復元される。
            Assert.Equal(3, _deep.Singletons.TopK(weights, 3).Length);

            // 各 item が独立に 1/2 で選ばれるとき、ちょうど「全部入り」になる確率は 2^-100000 = 0。
            Assert.Equal(0.0, _deep.Full.Probability(probabilities));

            // 族の上の一様分布での期待値。1 要素集合しかないので、どれを選んでも重みは 1。
            // 10 万段ぶんの浮動小数の足し込みなので、末尾の桁までは一致しない。
            Assert.Equal(1.0, _deep.Singletons.ExpectedValue(doubleWeights), 8);

            // item ごとの出現率。10 万個の集合のうち、item i を含むのは {i} の 1 つだけ。
            double[] frequency = _deep.Singletons.ItemFrequency();

            Assert.Equal(DeepZdd.VariableCount, frequency.Length);
            Assert.Equal(1.0 / DeepZdd.VariableCount, frequency[0], 12);
        }

        // ---- 可視化と統計 ----

        [Fact]
        public void DotOutputAndStatisticsFinish()
        {
            // 出力は溜め込まずに流すので、10 万段でも書き切れる（受け側は捨てる）。
            _deep.Full.WriteDot(TextWriter.Null);
            _deep.PowerSet.WriteDot(TextWriter.Null);

            ZddStatistics statistics = _deep.Manager.GetStatistics();

            // 3 つの族で 10 万段ずつ、ただし最下段の 1 個だけは共有される:
            // 「全部入り」と「1 要素集合の集まり」は、いちばん葉に近い段では
            // どちらも (0-枝 = ⊥, 1-枝 = ⊤) の同じノードになる。正準形なので同じ ID が返る。
            Assert.Equal((3L * DeepZdd.VariableCount) - 1L, statistics.NodeCount);

            Assert.Equal(statistics.NodeCount, statistics.PeakNodeCount);
            Assert.InRange(statistics.UniqueTableLoadFactor, 0.0, UniqueTable.MaxLoadFactorPercent / 100.0);
            Assert.InRange(statistics.NodeTableLoadFactor, 0.0, 1.0);
            Assert.InRange(statistics.CacheHitRate, 0.0, 1.0);
        }

        // ---- 利用者の評価器 ----

        [Fact]
        public void AUserSuppliedEvaluatorFinish()
        {
            // 利用者が書いた評価器も同じ走査に乗るので、深さで落ちない。
            int visits = _deep.PowerSet.Evaluate<DepthEval, int>(default);

            Assert.Equal(DeepZdd.VariableCount, visits);
        }

        /// <summary>根から終端までの最大の段数を数えるだけの評価器。</summary>
        private readonly struct DepthEval : IDdEval<int>
        {
            public int EvalTerminal(bool isTrue) => 0;

            public int EvalNode(int item, int lo, int hi) => Math.Max(lo, hi) + 1;
        }
    }
}

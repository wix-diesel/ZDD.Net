using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Internal;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// 順位づけ（<see cref="Zdd.ElementAt"/> / <see cref="Zdd.IndexOf(IEnumerable{int}, ZddEnumerationOrder)"/>）と
    /// 一様サンプリング（<see cref="Zdd.Sample(Random)"/>）の検証。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>要は「列挙と食い違わないこと」</b>。順位づけは列挙を全部辿らずに k 番目を出す近道なので、
    /// 正しさの基準は M1-13 の列挙そのものである。したがって主な照合は
    /// 「<c>ElementAt(0..Count-1)</c> を並べたら <c>Sets()</c> と 1 個ずつ一致する」形になる。
    /// </para>
    /// <para>
    /// <b>一様性は検定で見る</b>。乱数を使う以上「たまたま偏る」ことは避けられないので、種を固定して
    /// 決定的にしたうえで、カイ二乗統計量が有意水準の閾値を下回ることを確かめる。閾値には余裕を持たせる
    /// （docs/ROADMAP.md M1-14）。
    /// </para>
    /// </remarks>
    public class RankingTests
    {
        /// <summary>照合に使う変数の個数の上限（docs/ROADMAP.md M1-14）。</summary>
        private const int MaxRankingVariableCount = BruteForceFamily.MaxPowerSetVariableCount;

        /// <summary>スタックオーバーフローの回帰テストで使う変数の個数（docs/PLAN.md §4.5）。</summary>
        private const int DeepVariableCount = 100_000;

        private static readonly ZddEnumerationOrder[] Orders =
        {
            ZddEnumerationOrder.Default,
            ZddEnumerationOrder.Lexicographic,
        };

        // ---- ElementAt が列挙と一致すること ----

        [Fact]
        public void EveryFamilyOfThreeVariablesIsUnrankedLikeItIsEnumerated()
        {
            const int VariableCount = 3;

            using ZddManager manager = new ZddManager(VariableCount);

            // 3 変数の族は 2^8 = 256 通り。すべて試せる。
            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertRankingMatchesEnumeration(ZddFamilies.Build(manager, family));
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void EveryFamilyOfFourVariablesIsUnrankedLikeItIsEnumerated()
        {
            const int VariableCount = FamilyCases.AllFamiliesVariableLimit;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertRankingMatchesEnumeration(ZddFamilies.Build(manager, family));
            }
        }

        [Fact]
        public void RankingMatchesTheEnumerationUpToSixteenVariables()
        {
            for (int variableCount = 0; variableCount <= MaxRankingVariableCount; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                foreach (BruteForceFamily family in Families(variableCount, seed: 1500 + variableCount))
                {
                    AssertRankingMatchesEnumeration(ZddFamilies.Build(manager, family));
                }
            }
        }

        [Fact]
        public void TerminalFamiliesAreRankedTheWayTheirDefinitionSays()
        {
            using ZddManager manager = new ZddManager(4);

            // {∅} は空集合を 1 つだけ持つので、0 番目が空集合で、それ以上は無い。
            Assert.Empty(manager.Base.ElementAt(0));
            Assert.Equal(BigInteger.Zero, manager.Base.IndexOf());
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Base.ElementAt(1));

            // ∅ は集合を 1 つも持たないので、どんな順位も範囲外。
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Empty.ElementAt(0));
            Assert.Equal(BigInteger.MinusOne, manager.Empty.IndexOf());

            // 変数を 1 つも持たないマネージャでも同じ。
            using ZddManager empty = new ZddManager(0);
            Assert.Empty(empty.Base.ElementAt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => empty.Empty.ElementAt(0));
        }

        [Fact]
        public void TheTwoOrdersNumberTheSameSetsDifferently()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd powerSet = PowerSetOf(manager);

            // 既定は指示ベクトルの辞書順（EnumerationTests と同じ並び）。
            Assert.Equal(new[] { 2 }, powerSet.ElementAt(1));

            // 列としての辞書順では 1 番目は {0}。
            Assert.Equal(new[] { 0 }, powerSet.ElementAt(1, ZddEnumerationOrder.Lexicographic));

            // 同じ集合でも、順序が違えば順位は違う。
            Assert.Equal(new BigInteger(3), powerSet.IndexOf(new[] { 1, 2 }));
            Assert.Equal(new BigInteger(6), powerSet.IndexOf(new[] { 1, 2 }, ZddEnumerationOrder.Lexicographic));
        }

        // ---- IndexOf ----

        [Fact]
        public void IndexOfIsTheInverseOfElementAtUpToTwelveVariables()
        {
            for (int variableCount = 0; variableCount <= FamilyCases.ExhaustiveVariableLimit; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                foreach (BruteForceFamily family in Families(variableCount, seed: 1510 + variableCount))
                {
                    Zdd zdd = ZddFamilies.Build(manager, family);

                    foreach (ZddEnumerationOrder order in Orders)
                    {
                        for (BigInteger k = BigInteger.Zero; k < zdd.Count; k++)
                        {
                            Assert.Equal(k, zdd.IndexOf(zdd.ElementAt(k, order), order));
                        }
                    }
                }
            }
        }

        [Fact]
        public void IndexOfReturnsMinusOneForSetsTheFamilyDoesNotHold()
        {
            for (int variableCount = 0; variableCount <= FamilyCases.ExhaustiveVariableLimit; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                foreach (BruteForceFamily family in Families(variableCount, seed: 1520 + variableCount))
                {
                    Zdd zdd = ZddFamilies.Build(manager, family);

                    foreach (ZddEnumerationOrder order in Orders)
                    {
                        // 全部分集合を試す。属する集合には 0..Count-1 の順位が、
                        // 属さない集合には -1 が返る。
                        foreach (int mask in FamilyCases.AllSubsets(variableCount))
                        {
                            int[] set = ItemsOf(mask);
                            BigInteger index = zdd.IndexOf(set, order);

                            if (!family.Contains(mask))
                            {
                                Assert.Equal(BigInteger.MinusOne, index);
                                Assert.False(zdd.Contains(set));
                                continue;
                            }

                            Assert.InRange(index, BigInteger.Zero, zdd.Count - BigInteger.One);
                            Assert.Equal(set, zdd.ElementAt(index, order));
                        }
                    }
                }
            }
        }

        [Fact]
        public void IndexOfAcceptsItemsInAnyOrderAndIgnoresRepeats()
        {
            using ZddManager manager = new ZddManager(6);

            Zdd family = ZddFamilies.Build(manager, new[] { 1, 4, 5 }, new[] { 0 }, Array.Empty<int>());

            BigInteger expected = family.IndexOf(new[] { 1, 4, 5 });

            Assert.Equal(expected, family.IndexOf(5, 1, 4));
            Assert.Equal(expected, family.IndexOf(4, 4, 1, 5, 5, 5));
            Assert.Equal(expected, family.IndexOf(new List<int> { 5, 4, 1 }));

            // 空の集合は「空集合の順位」を問う。この族では先頭に来る。
            Assert.Equal(BigInteger.Zero, family.IndexOf());

            // 部分集合でも上位集合でも「その集合そのもの」でなければ順位は無い。
            Assert.Equal(BigInteger.MinusOne, family.IndexOf(1, 4));
            Assert.Equal(BigInteger.MinusOne, family.IndexOf(1, 4, 5, 3));
        }

        // ---- 大きすぎて数え上げられない族 ----

        [Fact]
        public void UnrankingWorksOnFamiliesWhoseCardinalityDoesNotFitInALong()
        {
            const int VariableCount = 100;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd powerSet = PowerSetOf(manager);

            // 2^100 個の集合。long はおろか、列挙で辿り着ける範囲でもない。
            BigInteger count = BigInteger.Pow(2, VariableCount);
            Assert.Equal(count, powerSet.Count);
            Assert.True(count > new BigInteger(long.MaxValue));

            // 冪集合の既定の順序は指示ベクトルの辞書順そのものなので、順位の 2 進表現がそのまま答になる。
            // 先頭は空集合、末尾は全体集合。
            Assert.Empty(powerSet.ElementAt(0));
            Assert.Equal(Enumerable.Range(0, VariableCount), powerSet.ElementAt(count - BigInteger.One));

            // 真ん中あたりの、long に収まらない順位でも O(変数の個数) で取り出せる。
            foreach (BigInteger index in new[] { count / 2, count / 3, count - new BigInteger(12345) })
            {
                int[] set = powerSet.ElementAt(index);

                Assert.Equal(ItemsOfIndicator(VariableCount, index), set);
                Assert.Equal(index, powerSet.IndexOf(set));
            }

            // サンプリングも同じ表の上に乗っているので、同じ大きさの族から引ける。
            Random random = new Random(20260830);
            int[][] sample = powerSet.Sample(16, random);

            Assert.Equal(16, sample.Length);
            Assert.All(sample, set => Assert.True(powerSet.Contains(set)));

            // 2^100 個から 16 個引いて全部同じ、ということはまず起きない。
            Assert.True(sample.Select(Key).Distinct().Count() > 1);
        }

        // ---- サンプリング ----

        [Fact]
        public void SamplingAlwaysReturnsASetTheFamilyHolds()
        {
            const int VariableCount = 12;

            using ZddManager manager = new ZddManager(VariableCount);

            Random random = new Random(1530);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 8, seed: 1531))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                if (zdd.IsEmpty)
                {
                    continue;
                }

                foreach (int[] set in zdd.Sample(32, random))
                {
                    Assert.True(zdd.Contains(set));
                    Assert.Equal(set.OrderBy(item => item).ToArray(), set);
                }

                Assert.True(zdd.Contains(zdd.Sample(random)));
            }
        }

        [Fact]
        public void SamplingASingletonFamilyAlwaysReturnsThatSet()
        {
            using ZddManager manager = new ZddManager(5);

            Zdd family = manager.Singleton(3);
            Random random = new Random(1540);

            // 選べる集合が 1 つしかないなら、乱数が何を返そうと答は 1 つ。
            Assert.All(family.Sample(20, random), set => Assert.Equal(new[] { 3 }, set));
        }

        [Fact]
        public void SamplingReturnsAFreshArrayEveryTime()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd powerSet = PowerSetOf(manager);
            int[][] sample = powerSet.Sample(16, new Random(1550));

            // 経路の作業配列を使い回していると、返した配列が全部同じものになる。
            Assert.Equal(sample.Length, sample.Distinct(ReferenceEqualityComparer.Instance).Count());

            // 受け取った配列は呼び出し側のもの。書き換えても族には影響しない。
            foreach (int[] set in sample)
            {
                Array.Fill(set, -1);
            }

            Assert.Equal(new BigInteger(16), powerSet.Count);
        }

        [Fact]
        public void SamplingWithTheSameSeedIsDeterministic()
        {
            using ZddManager manager = new ZddManager(8);

            Zdd powerSet = PowerSetOf(manager);

            Assert.Equal(
                powerSet.Sample(50, new Random(1560)).Select(Key).ToArray(),
                powerSet.Sample(50, new Random(1560)).Select(Key).ToArray());

            // 種が違えば（族が十分大きいので）並びも違う。
            Assert.NotEqual(
                powerSet.Sample(50, new Random(1560)).Select(Key).ToArray(),
                powerSet.Sample(50, new Random(1561)).Select(Key).ToArray());
        }

        [Fact]
        public void SamplingIsUniformEnoughToPassAChiSquaredTest()
        {
            const int VariableCount = 5;
            const int Draws = 32_000;

            using ZddManager manager = new ZddManager(VariableCount);

            // 32 個の集合を持つ族（冪集合）。1 つあたり期待度数 1000 で、検定に十分な大きさになる。
            Zdd powerSet = PowerSetOf(manager);
            int categories = 1 << VariableCount;

            int[] observed = new int[categories];

            foreach (int[] set in powerSet.Sample(Draws, new Random(1570)))
            {
                observed[BruteForceFamily.MaskOf(VariableCount, set)]++;
            }

            // どの集合も 1 度は出ているはず（期待度数 1000 で 0 回は天文学的にありえない）。
            Assert.All(observed, count => Assert.True(count > 0));

            double expected = (double)Draws / categories;
            double chiSquared = observed.Sum(count => ((count - expected) * (count - expected)) / expected);

            // 自由度 31 のカイ二乗分布の上側 0.1% 点は約 61.1。閾値には余裕を持たせて
            // 「偶然の偏りで落ちる」ことを避ける（種は固定なので、通れば常に通る）。
            Assert.True(
                chiSquared < 61.1,
                $"The chi-squared statistic was {chiSquared:F2} over {categories} categories, which suggests the sampling is biased.");
        }

        [Fact]
        public void SamplingIsUniformEnoughToPassAChiSquaredTestOnALopsidedFamily()
        {
            const int Draws = 30_000;

            using ZddManager manager = new ZddManager(6);

            // 濃度が 2 の冪でない族（棄却法が実際に棄却する形）。要素数が 3 の集合は 20 個ある。
            Zdd family = ZddFamilies.Build(
                manager,
                Subsets(6, size: 3).Select(mask => ItemsOf(mask)).ToArray());

            Assert.Equal(new BigInteger(20), family.Count);

            Dictionary<string, int> observed = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (int[] set in family.Sample(Draws, new Random(1580)))
            {
                string key = Key(set);
                observed[key] = observed.TryGetValue(key, out int seen) ? seen + 1 : 1;
            }

            Assert.Equal(20, observed.Count);

            double expected = Draws / 20.0;
            double chiSquared = observed.Values.Sum(count => ((count - expected) * (count - expected)) / expected);

            // 自由度 19 の上側 0.1% 点は約 43.8。
            Assert.True(
                chiSquared < 43.8,
                $"The chi-squared statistic was {chiSquared:F2} over 20 categories, which suggests the sampling is biased.");
        }

        // ---- BigInteger の一様乱数 ----

        [Fact]
        public void TheUniformBigIntegerSourceStaysInsideItsBound()
        {
            Random random = new Random(1590);

            // 2 の冪、2 の冪 ± 1、桁あふれの境目。棄却法が効く形と効かない形の両方を通す。
            foreach (BigInteger bound in Bounds())
            {
                UniformBigInteger uniform = new UniformBigInteger(bound);

                for (int i = 0; i < 200; i++)
                {
                    BigInteger value = uniform.Next(random);

                    Assert.True(value >= BigInteger.Zero, $"{value} is negative for bound {bound}.");
                    Assert.True(value < bound, $"{value} is not below the bound {bound}.");
                }
            }
        }

        [Fact]
        public void TheUniformBigIntegerSourceReturnsZeroWhenOnlyZeroFits()
        {
            UniformBigInteger uniform = new UniformBigInteger(BigInteger.One);

            // 上限が 1 なら答は 0 しかない。乱数を 1 ビットも引かずに返る。
            Assert.All(
                Enumerable.Range(0, 10).Select(_ => uniform.Next(new Random(1600))),
                value => Assert.Equal(BigInteger.Zero, value));
        }

        [Fact]
        public void TheUniformBigIntegerSourceCoversTheWholeRangeEvenly()
        {
            // 3 は 2 の冪ではないので、2 ビット引いて 3 が出たら捨てる形になる。
            // 剰余で済ませていれば 0 だけが 2 倍出るので、そこを見張る。
            UniformBigInteger uniform = new UniformBigInteger(new BigInteger(3));
            Random random = new Random(1610);

            int[] observed = new int[3];
            for (int i = 0; i < 30_000; i++)
            {
                observed[(int)uniform.Next(random)]++;
            }

            double expected = 10_000.0;
            double chiSquared = observed.Sum(count => ((count - expected) * (count - expected)) / expected);

            // 自由度 2 の上側 0.1% 点は約 13.8。剰余の偏り（0 が 1.5 倍）なら数千の統計量になる。
            Assert.True(chiSquared < 13.8, $"The chi-squared statistic was {chiSquared:F2}, which suggests a biased mapping.");
        }

        // ---- 誤用 ----

        [Fact]
        public void RankingRejectsIndicesOutsideTheFamily()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd family = ZddFamilies.Build(manager, new[] { 0 }, new[] { 1, 2 });

            Assert.Equal(new BigInteger(2), family.Count);

            ArgumentOutOfRangeException error =
                Assert.Throws<ArgumentOutOfRangeException>(() => family.ElementAt(2));
            Assert.Equal("index", error.ParamName);

            Assert.Throws<ArgumentOutOfRangeException>(() => family.ElementAt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => family.ElementAt(BigInteger.Pow(10, 30)));
        }

        [Fact]
        public void SamplingAnEmptyFamilyIsAnError()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd empty = manager.Empty;

            Assert.Throws<InvalidOperationException>(() => empty.Sample(new Random(1620)));
            Assert.Throws<InvalidOperationException>(() => empty.Sample(4, new Random(1620)));

            // 0 個であっても、空の族からは取り出せないものとして例外にする（doc のとおり）。
            Assert.Throws<InvalidOperationException>(() => empty.Sample(0, new Random(1620)));

            // 空でない族からなら 0 個は空の並び。
            Assert.Empty(manager.Base.Sample(0, new Random(1620)));
        }

        [Fact]
        public void RankingRejectsBadArguments()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd family = PowerSetOf(manager);

            Assert.Throws<ArgumentNullException>(() => family.Sample(null!));
            Assert.Throws<ArgumentNullException>(() => family.Sample(3, null!));
            Assert.Throws<ArgumentNullException>(() => family.IndexOf((IEnumerable<int>)null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => family.Sample(-1, new Random(1630)));

            // 宇宙の外の item は Contains と同じく例外（-1 では返さない。渡し間違いだから）。
            Assert.Throws<ArgumentOutOfRangeException>(() => family.IndexOf(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => family.IndexOf(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => family.IndexOf(new List<int> { 0, 9 }));

            // 順序の検査は列挙と同じ場所・同じ引数名で行う。
            ArgumentOutOfRangeException error =
                Assert.Throws<ArgumentOutOfRangeException>(() => family.ElementAt(0, (ZddEnumerationOrder)7));
            Assert.Equal("order", error.ParamName);
            Assert.Throws<ArgumentOutOfRangeException>(() => family.IndexOf(new[] { 0 }, (ZddEnumerationOrder)7));
        }

        [Fact]
        public void RankingRejectsDefaultHandlesAndDisposedManagers()
        {
            Zdd invalid = default;

            Assert.Throws<InvalidOperationException>(() => invalid.ElementAt(0));
            Assert.Throws<InvalidOperationException>(() => invalid.IndexOf());
            Assert.Throws<InvalidOperationException>(() => invalid.Sample(new Random(1640)));
            Assert.Throws<InvalidOperationException>(() => invalid.Sample(2, new Random(1640)));

            ZddManager manager = new ZddManager(4);
            Zdd family = PowerSetOf(manager);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => family.ElementAt(0));
            Assert.Throws<ObjectDisposedException>(() => family.IndexOf());
            Assert.Throws<ObjectDisposedException>(() => family.Sample(new Random(1640)));
            Assert.Throws<ObjectDisposedException>(() => family.Sample(2, new Random(1640)));
        }

        // ---- 深い ZDD（docs/PLAN.md §4.5 の回帰テスト）----

        [Fact]
        [Trait("Category", "Slow")]
        public void ADeepFamilyDoesNotOverflowTheStack()
        {
            using ZddManager manager = new ZddManager(DeepVariableCount);

            // 変数 10 万個すべてを含む集合 1 つだけの族。ノードが 10 万段に連なる。
            Zdd single = SingleFullSet(manager);

            // 濃度の表（ボトムアップの走査）も、そこを降りる経路も、明示スタック
            // ないし 1 本道なので、10 万段でも落ちない。
            int[] only = single.ElementAt(0);
            Assert.Equal(DeepVariableCount, only.Length);
            Assert.Equal(BigInteger.Zero, single.IndexOf(only));
            Assert.Equal(only, single.Sample(new Random(1650)));
            Assert.Equal(only, single.ElementAt(0, ZddEnumerationOrder.Lexicographic));

            // 1 要素集合ばかりを 10 万個持つ族。こちらは経路が枝分かれし、
            // 0-枝の連なりを辿る辞書順の側もちょうど 10 万段になる。
            Zdd singletons = AllSingletons(manager);
            Assert.Equal(new BigInteger(DeepVariableCount), singletons.Count);

            // 既定は指示ベクトルの辞書順なので、葉側の item から先に出る。
            Assert.Equal(new[] { DeepVariableCount - 1 }, singletons.ElementAt(0));
            Assert.Equal(new[] { 0 }, singletons.ElementAt(DeepVariableCount - 1));

            // 列としての辞書順では逆に、item 0 から始まる集合が先。
            Assert.Equal(new[] { 0 }, singletons.ElementAt(0, ZddEnumerationOrder.Lexicographic));
            Assert.Equal(
                new[] { DeepVariableCount - 1 },
                singletons.ElementAt(DeepVariableCount - 1, ZddEnumerationOrder.Lexicographic));

            foreach (ZddEnumerationOrder order in Orders)
            {
                Assert.Equal(
                    new BigInteger(7),
                    singletons.IndexOf(singletons.ElementAt(7, order), order));
            }

            int[] sampled = singletons.Sample(new Random(1651));
            Assert.Single(sampled);
            Assert.True(singletons.Contains(sampled));
        }

        // ---- 補助 ----

        /// <summary>
        /// 順位づけが列挙とぴったり一致すること（並び・逆写像・両端）。
        /// </summary>
        private static void AssertRankingMatchesEnumeration(Zdd zdd)
        {
            foreach (ZddEnumerationOrder order in Orders)
            {
                int[][] enumerated = zdd.Sets(order).ToArray();

                Assert.Equal(new BigInteger(enumerated.Length), zdd.Count);

                for (int k = 0; k < enumerated.Length; k++)
                {
                    int[] unranked = zdd.ElementAt(k, order);

                    Assert.Equal(enumerated[k], unranked);
                    Assert.Equal(new BigInteger(k), zdd.IndexOf(unranked, order));
                }

                // 端の外は範囲外（空の族なら 0 番目からして範囲外）。
                Assert.Throws<ArgumentOutOfRangeException>(() => zdd.ElementAt(enumerated.Length, order));
            }
        }

        /// <summary>照合に使う族の並び（境界 3 つ＋ランダム）。</summary>
        private static IEnumerable<BruteForceFamily> Families(int variableCount, int seed)
        {
            yield return BruteForceFamily.Empty(variableCount);
            yield return BruteForceFamily.Base(variableCount);
            yield return BruteForceFamily.PowerSet(variableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(variableCount, 4, seed))
            {
                yield return family;
            }
        }

        /// <summary>一様乱数の検査に使う上限（2 の冪の前後と、桁が大きいもの）。</summary>
        private static IEnumerable<BigInteger> Bounds()
        {
            foreach (int bound in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 255, 256, 257 })
            {
                yield return new BigInteger(bound);
            }

            yield return new BigInteger(long.MaxValue);
            yield return BigInteger.Pow(2, 64);
            yield return BigInteger.Pow(2, 64) + BigInteger.One;
            yield return BigInteger.Pow(10, 30);
        }

        /// <summary>ビットマスクを昇順の item 列に直す。</summary>
        private static int[] ItemsOf(int mask)
        {
            List<int> items = new List<int>();

            for (int item = 0; mask >> item != 0; item++)
            {
                if ((mask & (1 << item)) != 0)
                {
                    items.Add(item);
                }
            }

            return items.ToArray();
        }

        /// <summary>
        /// 冪集合の既定の順序での順位を、そのまま指示ベクトルとして読んだ集合。
        /// </summary>
        /// <remarks>
        /// 既定の順序は指示ベクトルの辞書順で、item 0 が最上位ビットに当たる。
        /// </remarks>
        private static int[] ItemsOfIndicator(int variableCount, BigInteger index)
        {
            List<int> items = new List<int>();

            for (int item = 0; item < variableCount; item++)
            {
                if (!((index >> (variableCount - 1 - item)) & BigInteger.One).IsZero)
                {
                    items.Add(item);
                }
            }

            return items.ToArray();
        }

        /// <summary>要素数がちょうど <paramref name="size"/> の部分集合をビットマスクで返す。</summary>
        private static IEnumerable<int> Subsets(int variableCount, int size)
        {
            for (int mask = 0; mask < 1 << variableCount; mask++)
            {
                if (BitOperations.PopCount((uint)mask) == size)
                {
                    yield return mask;
                }
            }
        }

        /// <summary>集合を並びごと比べられる文字列に直す（アサーションの読みやすさのため）。</summary>
        private static string Key(int[] set) => string.Join(",", set);

        /// <summary>全変数の冪集合 <c>2^U</c>。ノードは変数の個数ぶんしかない。</summary>
        private static Zdd PowerSetOf(ZddManager manager)
        {
            Zdd result = manager.Base;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                result = manager.CreateNode(item, result, result);
            }

            return result;
        }

        /// <summary>全変数を含む集合 1 つだけの族 <c>{{0, …, n-1}}</c>。</summary>
        private static Zdd SingleFullSet(ZddManager manager)
        {
            Zdd result = manager.Base;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                result = manager.CreateNode(item, manager.Empty, result);
            }

            return result;
        }

        /// <summary>1 要素集合だけを集めた族 <c>{{0}, {1}, …, {n-1}}</c>。</summary>
        /// <remarks>
        /// ノードは変数の個数ぶん連なるが、濃度はどの段でも高々 <c>n</c> なので、
        /// 深さの回帰テストに使っても <see cref="BigInteger"/> の桁で重くならない。
        /// </remarks>
        private static Zdd AllSingletons(ZddManager manager)
        {
            Zdd result = manager.Empty;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                result = manager.CreateNode(item, result, manager.Base);
            }

            return result;
        }
    }
}

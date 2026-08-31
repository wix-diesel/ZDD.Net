using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// 重み最適化（<see cref="Zdd.MaxWeight(ReadOnlySpan{int})"/> /
    /// <see cref="Zdd.MinWeight(ReadOnlySpan{int})"/> / <see cref="Zdd.TopK(ReadOnlySpan{int}, int)"/>）と、
    /// 確率・期待値・頻度（<see cref="Zdd.Probability"/> / <see cref="Zdd.ExpectedValue"/> /
    /// <see cref="Zdd.ItemFrequency"/>）の検証。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>照合相手は必ず「全部並べて選んだ答」</b>（docs/ROADMAP.md M1-15）。ここで検証したいのは
    /// 「全解を並べずに求めた答が、並べて求めた答と一致すること」なので、期待値の側は
    /// <see cref="BruteForceFamily"/> の集合を素直に舐めて計算する。変数 12 個までなら
    /// 2^12 = 4096 通りしかないので、総当たりが現実的に回る。
    /// </para>
    /// <para>
    /// <b>同点の扱い</b>: 最大・最小は「重み」を照合する（同じ重みの集合が複数あるとき、
    /// どれが返るかは API が規定しないため）。返った集合については「族に属すること」と
    /// 「その重みが報告された重みと一致すること」を確かめる。<see cref="Zdd.TopK(ReadOnlySpan{int}, int)"/>
    /// も同じで、照合するのは<b>重みの並び</b>である。
    /// </para>
    /// </remarks>
    public class WeightOptimizationTests
    {
        /// <summary>総当たり照合で回す変数の個数の上限（docs/ROADMAP.md M1-15）。</summary>
        private const int MaxExhaustiveVariableCount = FamilyCases.ExhaustiveVariableLimit;

        /// <summary>スタックオーバーフローの回帰テストで使う変数の個数（docs/PLAN.md §4.5）。</summary>
        private const int DeepVariableCount = 100_000;

        /// <summary>浮動小数の照合に許す誤差。</summary>
        private const double Tolerance = 1e-9;

        // ---- 最大・最小が総当たりと一致すること ----

        [Fact]
        public void EveryFamilyOfThreeVariablesIsOptimizedLikeTheNaiveSearch()
        {
            const int VariableCount = 3;

            using ZddManager manager = new ZddManager(VariableCount);
            Random random = new Random(1500);

            // 3 変数の族は 2^8 = 256 通り。すべて試せる。
            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertOptimizationMatchesNaive(manager, family, RandomWeights(VariableCount, random));
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void EveryFamilyOfFourVariablesIsOptimizedLikeTheNaiveSearch()
        {
            const int VariableCount = FamilyCases.AllFamiliesVariableLimit;

            using ZddManager manager = new ZddManager(VariableCount);
            Random random = new Random(1501);

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertOptimizationMatchesNaive(manager, family, RandomWeights(VariableCount, random));
            }
        }

        [Fact]
        public void OptimizationMatchesTheNaiveSearchUpToTwelveVariables()
        {
            for (int variableCount = 0; variableCount <= MaxExhaustiveVariableCount; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);
                Random random = new Random(1502 + variableCount);

                // 境界（{∅} / 冪集合）と、ランダムな族を混ぜて回す（∅ には最適解が無いので別のテスト）。
                AssertOptimizationMatchesNaive(
                    manager,
                    BruteForceFamily.Base(variableCount),
                    RandomWeights(variableCount, random));

                AssertOptimizationMatchesNaive(
                    manager,
                    BruteForceFamily.PowerSet(variableCount),
                    RandomWeights(variableCount, random));

                foreach (BruteForceFamily family in FamilyCases.RandomFamilies(
                    variableCount,
                    8,
                    seed: 1600 + variableCount))
                {
                    if (family.IsEmpty)
                    {
                        continue;
                    }

                    AssertOptimizationMatchesNaive(manager, family, RandomWeights(variableCount, random));
                }
            }
        }

        [Fact]
        public void AllNegativeWeightsMakeTheEmptySetTheBestWhenTheFamilyHoldsIt()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            // 冪集合はどの部分集合も持つ。重みが全部負なら最大は ∅、最小は全体集合 U。
            Zdd powerSet = PowerSetOf(manager);
            int[] weights = Enumerable.Range(0, VariableCount).Select(item => -(item + 1)).ToArray();

            WeightedSet<int> best = powerSet.MaxWeight(weights);
            Assert.Equal(0, best.Weight);
            Assert.Empty(best.Items);

            WeightedSet<int> worst = powerSet.MinWeight(weights);
            Assert.Equal(weights.Sum(), worst.Weight);
            Assert.Equal(Enumerable.Range(0, VariableCount), worst.Items);
        }

        [Fact]
        public void TheOptimumIsTheFirstOneInTheEnumerationWhenWeightsTie()
        {
            const int VariableCount = 4;

            using ZddManager manager = new ZddManager(VariableCount);

            // 重みが全部 0 なら、どの集合も同点。API は「既定の列挙順で最初のもの」を返すと約束している。
            Zdd family = ZddFamilies.Build(manager, new[] { 1, 3 }, new[] { 0 }, new[] { 2 });
            int[] weights = new int[VariableCount];

            int[] first = family.Sets().First();

            Assert.Equal(first, family.MaxWeight(weights).Items);
            Assert.Equal(first, family.MinWeight(weights).Items);
        }

        [Fact]
        public void TheBuiltInWeightTypesAgreeWithOneAnother()
        {
            const int VariableCount = 8;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 8, seed: 1700))
            {
                if (family.IsEmpty)
                {
                    continue;
                }

                Zdd zdd = ZddFamilies.Build(manager, family);
                int[] weights = RandomWeights(VariableCount, new Random(1701 + family.Count));

                WeightedSet<int> asInt = zdd.MaxWeight(weights);
                WeightedSet<long> asLong = zdd.MaxWeight(weights.Select(w => (long)w).ToArray());
                WeightedSet<double> asDouble = zdd.MaxWeight(weights.Select(w => (double)w).ToArray());
                WeightedSet<BigInteger> asBig = zdd.MaxWeight<BigInteger, BigIntegerWeightOps>(
                    weights.Select(w => new BigInteger(w)).ToArray());

                Assert.Equal(asInt.Weight, asLong.Weight);
                Assert.Equal(asInt.Weight, asDouble.Weight);
                Assert.Equal(new BigInteger(asInt.Weight), asBig.Weight);

                Assert.Equal(asInt.Items, asLong.Items);
                Assert.Equal(asInt.Items, asDouble.Items);
                Assert.Equal(asInt.Items, asBig.Items);
            }
        }

        [Fact]
        public void TheIntegerWeightTypesRefuseToOverflowSilently()
        {
            using ZddManager manager = new ZddManager(2);

            Zdd family = ZddFamilies.Build(manager, new[] { 0, 1 });

            // 2 つ足すと int の範囲を超える。黙って折り返すと「最大重みが負」という静かな誤りになる。
            Assert.Throws<OverflowException>(() => family.MaxWeight(int.MaxValue, 1));

            // long なら収まる。
            Assert.Equal((long)int.MaxValue + 1, family.MaxWeight((long)int.MaxValue, 1L).Weight);
        }

        // ---- TopK ----

        [Fact]
        public void TopKMatchesTheSortedNaiveEnumeration()
        {
            for (int variableCount = 1; variableCount <= 10; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);
                Random random = new Random(1800 + variableCount);

                foreach (BruteForceFamily family in FamilyCases.RandomFamilies(
                    variableCount,
                    6,
                    seed: 1900 + variableCount))
                {
                    int[] weights = RandomWeights(variableCount, random);
                    Zdd zdd = ZddFamilies.Build(manager, family);

                    // 素朴側: 族の集合を重みの降順に並べる。
                    int[] sorted = family.Masks
                        .Select(mask => WeightOf(mask, weights))
                        .OrderByDescending(weight => weight)
                        .ToArray();

                    foreach (int k in new[] { 0, 1, 2, 5, 17, family.Count + 3 })
                    {
                        WeightedSet<int>[] top = zdd.TopK(weights, k);

                        // 個数は「k と濃度の小さい方」。
                        Assert.Equal(Math.Min(k, family.Count), top.Length);

                        // 重みの並びは、全列挙を降順に並べた先頭 k 個とぴったり一致する（同値も含めて）。
                        Assert.Equal(sorted.Take(top.Length), top.Select(entry => entry.Weight));

                        // 返る集合はどれも族に属し、報告された重みを持ち、互いに異なる。
                        foreach (WeightedSet<int> entry in top)
                        {
                            Assert.True(zdd.Contains(entry.Items));
                            Assert.Equal(entry.Weight, entry.Items.Sum(item => weights[item]));
                            Assert.Equal(entry.Items.Length, entry.Size);
                        }

                        Assert.Equal(
                            top.Length,
                            top.Select(entry => string.Join(',', entry.Items)).Distinct().Count());
                    }
                }
            }
        }

        [Fact]
        public void TopKWithOneEntryAgreesWithMaxWeight()
        {
            const int VariableCount = 9;

            using ZddManager manager = new ZddManager(VariableCount);
            Random random = new Random(2000);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 8, seed: 2001))
            {
                if (family.IsEmpty)
                {
                    continue;
                }

                Zdd zdd = ZddFamilies.Build(manager, family);
                int[] weights = RandomWeights(VariableCount, random);

                Assert.Equal(zdd.MaxWeight(weights).Weight, zdd.TopK(weights, 1).Single().Weight);
            }
        }

        // ---- 確率 ----

        [Fact]
        public void ProbabilityMatchesTheNaiveEnumeration()
        {
            for (int variableCount = 0; variableCount <= 10; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);
                Random random = new Random(2100 + variableCount);

                foreach (BruteForceFamily family in Families(variableCount, seed: 2200 + variableCount))
                {
                    double[] probabilities = RandomProbabilities(variableCount, random);
                    Zdd zdd = ZddFamilies.Build(manager, family);

                    Assert.Equal(
                        NaiveProbability(family, probabilities),
                        zdd.Probability(probabilities),
                        Tolerance);
                }
            }
        }

        [Fact]
        public void ProbabilityFollowsItsDefinitionOnTheBoundaries()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);
            double[] probabilities = { 0.1, 0.25, 0.5, 0.75, 0.9 };

            // 空の族には集合が無いので、どんな選び方も属さない。
            Assert.Equal(0.0, manager.Empty.Probability(probabilities));

            // {∅} は「どの item も選ばれない」確率そのもの。
            double nothingChosen = probabilities.Aggregate(1.0, (product, p) => product * (1.0 - p));
            Assert.Equal(nothingChosen, manager.Base.Probability(probabilities), Tolerance);

            // 冪集合はどんな選び方も属するので 1。
            Assert.Equal(1.0, PowerSetOf(manager).Probability(probabilities), Tolerance);

            // 補と足すと 1 になる（族と補は宇宙を二分する）。
            Zdd family = ZddFamilies.Build(manager, new[] { 0, 3 }, new[] { 1 }, new[] { 2, 4 });
            Assert.Equal(
                1.0,
                family.Probability(probabilities) + family.Complement().Probability(probabilities),
                Tolerance);
        }

        [Fact]
        public void CertainItemsMakeTheUniverseTheOnlyOutcome()
        {
            const int VariableCount = 4;

            using ZddManager manager = new ZddManager(VariableCount);

            double[] certain = Enumerable.Repeat(1.0, VariableCount).ToArray();
            double[] impossible = new double[VariableCount];
            int[] universe = Enumerable.Range(0, VariableCount).ToArray();

            // p = 1 なら、選ばれる集合は必ず全体集合 U。したがって確率は「U が族に属するか」に等しく、
            // 族が空でないことでは 1 にならない（宇宙はマネージャの全変数であるため）。
            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                Assert.Equal(zdd.Contains(universe) ? 1.0 : 0.0, zdd.Probability(certain));

                // p = 0 なら、選ばれるのは必ず空集合。
                Assert.Equal(zdd.Contains(Array.Empty<int>()) ? 1.0 : 0.0, zdd.Probability(impossible));
            }
        }

        [Fact]
        public void ProbabilityIsOneForAnyNonEmptyUpwardClosedFamily()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            // ネットワーク信頼性で扱う族（「s–t を連結にする辺集合」）は上に閉じている。
            // p = 1 ならどの辺も生きているので、信頼性は 1 になる。
            Zdd minimalPaths = ZddFamilies.Build(manager, new[] { 0, 1 }, new[] { 2, 3, 4 });
            Zdd connected = minimalPaths.Product(PowerSetOf(manager));

            Assert.Equal(1.0, connected.Probability(Enumerable.Repeat(1.0, VariableCount).ToArray()), Tolerance);

            // 辺が半々で生きているときの信頼性も、全列挙と一致する。
            double[] half = Enumerable.Repeat(0.5, VariableCount).ToArray();
            Assert.Equal(
                NaiveProbability(ZddFamilies.ToBruteForce(connected), half),
                connected.Probability(half),
                Tolerance);
        }

        // ---- 期待値と頻度 ----

        [Fact]
        public void ExpectedValueAndItemFrequencyMatchTheNaiveEnumeration()
        {
            for (int variableCount = 1; variableCount <= 10; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);
                Random random = new Random(2300 + variableCount);

                foreach (BruteForceFamily family in Families(variableCount, seed: 2400 + variableCount))
                {
                    if (family.IsEmpty)
                    {
                        continue;
                    }

                    Zdd zdd = ZddFamilies.Build(manager, family);
                    double[] weights = RandomWeights(variableCount, random).Select(w => (double)w).ToArray();

                    double[] frequency = zdd.ItemFrequency();
                    Assert.Equal(variableCount, frequency.Length);

                    for (int item = 0; item < variableCount; item++)
                    {
                        double expected = (double)family.Masks.Count(mask => (mask & (1 << item)) != 0)
                            / family.Count;

                        Assert.Equal(expected, frequency[item], Tolerance);
                    }

                    double average = family.Masks.Sum(mask => (double)WeightOf(mask, weights)) / family.Count;
                    Assert.Equal(average, zdd.ExpectedValue(weights), Tolerance);
                }
            }
        }

        [Fact]
        public void ItemFrequencyFollowsItsDefinitionOnKnownFamilies()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            // {∅} の唯一の集合は空集合なので、どの item も入っていない。
            Assert.All(manager.Base.ItemFrequency(), value => Assert.Equal(0.0, value));
            Assert.Equal(0.0, manager.Base.ExpectedValue(new double[VariableCount]));

            // 冪集合では、どの item もちょうど半分の集合に入る。
            Assert.All(PowerSetOf(manager).ItemFrequency(), value => Assert.Equal(0.5, value, Tolerance));

            // 族が使っていない item の確率は 0。
            Zdd family = ZddFamilies.Build(manager, new[] { 0, 2 }, new[] { 2 });
            double[] frequency = family.ItemFrequency();

            Assert.Equal(0.5, frequency[0], Tolerance);
            Assert.Equal(1.0, frequency[2], Tolerance);
            Assert.Equal(0.0, frequency[1]);
            Assert.Equal(0.0, frequency[5]);
        }

        [Fact]
        public void ItemFrequencyStaysExactWhenTheCardinalityOverflowsADouble()
        {
            // 2^2000 は double.MaxValue（およそ 1.8 × 10^308）を大きく超える。
            // 個数を double で数えていれば inf / inf = NaN になるところ。
            const int VariableCount = 2000;

            using ZddManager manager = new ZddManager(VariableCount);

            Assert.All(PowerSetOf(manager).ItemFrequency(), value => Assert.Equal(0.5, value, Tolerance));
        }

        // ---- 利用者が定義する重み型 ----

        [Fact]
        public void AUserDefinedRationalWeightWorks()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            // 1/2, 1/3, … の重み。double では丸めが入るが、有理数なら厳密に比べられる。
            Rational[] weights = Enumerable.Range(0, VariableCount)
                .Select(item => new Rational(1, item + 2))
                .ToArray();

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 8, seed: 2500))
            {
                if (family.IsEmpty)
                {
                    continue;
                }

                Zdd zdd = ZddFamilies.Build(manager, family);

                WeightedSet<Rational> best = zdd.MaxWeight<Rational, RationalWeightOps>(weights);
                WeightedSet<Rational> worst = zdd.MinWeight<Rational, RationalWeightOps>(weights);

                Rational bestNaive = family.Masks
                    .Select(mask => RationalWeightOf(mask, weights))
                    .Aggregate((left, right) => RationalWeightOps.Compare(left, right) >= 0 ? left : right);

                Rational worstNaive = family.Masks
                    .Select(mask => RationalWeightOf(mask, weights))
                    .Aggregate((left, right) => RationalWeightOps.Compare(left, right) <= 0 ? left : right);

                Assert.Equal(0, RationalWeightOps.Compare(bestNaive, best.Weight));
                Assert.Equal(0, RationalWeightOps.Compare(worstNaive, worst.Weight));

                Assert.True(zdd.Contains(best.Items));
                Assert.True(zdd.Contains(worst.Items));
            }
        }

        [Fact]
        public void AUserDefinedLexicographicWeightWorks()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            // 主要素で比べ、同点なら副要素で比べる重み。double や long では表せない順序。
            Pair[] weights =
            {
                new Pair(1, 0),
                new Pair(1, 5),
                new Pair(0, 3),
                new Pair(2, -1),
                new Pair(0, 0),
            };

            Zdd family = ZddFamilies.Build(
                manager,
                new[] { 0 },
                new[] { 1 },
                new[] { 2, 4 },
                new[] { 3 },
                new[] { 0, 2 });

            // 主要素の最大は {3}（2）だが、{0, 2} と {1} は主要素 1 で並ぶ。副要素で {1}（5）が勝つ。
            Assert.Equal(new Pair(2, -1), family.MaxWeight<Pair, LexicographicWeightOps>(weights).Weight);
            Assert.Equal(new[] { 3 }, family.MaxWeight<Pair, LexicographicWeightOps>(weights).Items);

            WeightedSet<Pair>[] top = family.TopK<Pair, LexicographicWeightOps>(weights, 3);

            Assert.Equal(new Pair(2, -1), top[0].Weight);
            Assert.Equal(new Pair(1, 5), top[1].Weight);
            Assert.Equal(new Pair(1, 3), top[2].Weight);
            Assert.Equal(new[] { 1 }, top[1].Items);
            Assert.Equal(new[] { 0, 2 }, top[2].Items);
        }

        // ---- 境界と検証 ----

        [Fact]
        public void TerminalFamiliesFollowTheirDefinition()
        {
            using ZddManager manager = new ZddManager(3);
            int[] weights = { 3, -1, 4 };

            // {∅} の唯一の集合は空集合。重みは 0（加法の単位元）。
            WeightedSet<int> best = manager.Base.MaxWeight(weights);
            Assert.Equal(0, best.Weight);
            Assert.Empty(best.Items);
            Assert.Equal(0, best.Size);
            Assert.Equal(0, manager.Base.MinWeight(weights).Weight);
            Assert.Equal(0, manager.Base.TopK(weights, 5).Single().Weight);

            // ∅ には集合が 1 つも無いので、最適解も期待値も定義できない。
            Assert.Throws<InvalidOperationException>(() => manager.Empty.MaxWeight(weights));
            Assert.Throws<InvalidOperationException>(() => manager.Empty.MinWeight(weights));
            Assert.Throws<InvalidOperationException>(() => manager.Empty.ItemFrequency());
            Assert.Throws<InvalidOperationException>(() => manager.Empty.ExpectedValue(1.0, 2.0, 3.0));

            // TopK と Probability は「1 つも無い」を素直に返せる。
            Assert.Empty(manager.Empty.TopK(weights, 5));
            Assert.Equal(0.0, manager.Empty.Probability(0.5, 0.5, 0.5));

            // 変数を 1 つも持たないマネージャでも同じ。
            using ZddManager empty = new ZddManager(0);
            Assert.Empty(empty.Base.MaxWeight(Array.Empty<int>()).Items);
            Assert.Equal(1.0, empty.Base.Probability(Array.Empty<double>()));
            Assert.Empty(empty.Base.ItemFrequency());
        }

        [Fact]
        public void WeightsOfTheWrongLengthAreRejected()
        {
            using ZddManager manager = new ZddManager(3);
            Zdd family = ZddFamilies.Build(manager, new[] { 0 }, new[] { 1, 2 });

            Assert.Throws<ArgumentException>(() => family.MaxWeight(1, 2));
            Assert.Throws<ArgumentException>(() => family.MinWeight(1, 2, 3, 4));
            Assert.Throws<ArgumentException>(() => family.TopK(new[] { 1, 2 }, 3));
            Assert.Throws<ArgumentException>(() => family.Probability(0.5, 0.5));
            Assert.Throws<ArgumentException>(() => family.ExpectedValue(0.5, 0.5));
        }

        [Fact]
        public void ProbabilitiesOutsideTheUnitIntervalAreRejected()
        {
            using ZddManager manager = new ZddManager(2);
            Zdd family = ZddFamilies.Build(manager, new[] { 0 });

            Assert.Throws<ArgumentOutOfRangeException>(() => family.Probability(-0.1, 0.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => family.Probability(0.5, 1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => family.Probability(double.NaN, 0.5));

            // 端は有効。
            Assert.Equal(1.0, family.Probability(1.0, 0.0));
        }

        [Fact]
        public void NegativeCountsAreRejected()
        {
            using ZddManager manager = new ZddManager(2);
            Zdd family = ZddFamilies.Build(manager, new[] { 0 });

            Assert.Throws<ArgumentOutOfRangeException>(() => family.TopK(new[] { 1, 1 }, -1));
        }

        [Fact]
        public void OptimizingADefaultHandleThrows()
        {
            Zdd invalid = default;

            Assert.Throws<InvalidOperationException>(() => invalid.MaxWeight(1));
            Assert.Throws<InvalidOperationException>(() => invalid.MinWeight(1));
            Assert.Throws<InvalidOperationException>(() => invalid.TopK(new[] { 1 }, 1));
            Assert.Throws<InvalidOperationException>(() => invalid.Probability(0.5));
            Assert.Throws<InvalidOperationException>(() => invalid.ExpectedValue(0.5));
            Assert.Throws<InvalidOperationException>(() => invalid.ItemFrequency());
        }

        [Fact]
        public void OptimizingAFamilyOfADisposedManagerThrows()
        {
            ZddManager manager = new ZddManager(3);
            Zdd family = manager.Singleton(1) | manager.Base;
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => family.MaxWeight(1, 2, 3));
            Assert.Throws<ObjectDisposedException>(() => family.MinWeight(1, 2, 3));
            Assert.Throws<ObjectDisposedException>(() => family.TopK(new[] { 1, 2, 3 }, 2));
            Assert.Throws<ObjectDisposedException>(() => family.Probability(0.5, 0.5, 0.5));
            Assert.Throws<ObjectDisposedException>(() => family.ExpectedValue(1.0, 1.0, 1.0));
            Assert.Throws<ObjectDisposedException>(() => family.ItemFrequency());
        }

        [Fact]
        public void TheReturnedSetIsOwnedByTheResultAndNotSharedWithTheFamily()
        {
            using ZddManager manager = new ZddManager(4);
            Zdd family = ZddFamilies.Build(manager, new[] { 0, 2 });

            WeightedSet<int> best = family.MaxWeight(1, 1, 1, 1);

            // 同じ結果からは同じ配列が返る（写しは作らない）。
            Assert.Same(best.Items, best.Items);

            best.Items[0] = 99;

            // 書き換えても族は変わらないし、次の呼び出しは新しい配列を返す。
            Assert.Equal(new[] { 0, 2 }, family.MaxWeight(1, 1, 1, 1).Items);
            Assert.NotSame(best.Items, family.MaxWeight(1, 1, 1, 1).Items);
        }

        // ---- 深い ZDD（スタックオーバーフロー回帰テスト） ----

        [Fact]
        public void DeepDiagramsDoNotOverflowTheStack()
        {
            // 変数 10 万。素直な再帰実装ならここで StackOverflowException になり、
            // .NET では catch できずプロセスごと落ちる（docs/PLAN.md §4.5）。
            using ZddManager manager = new ZddManager(DeepVariableCount);

            // 集合を 1 つだけ持つ族 {{0, 1, …, 99999}}。10 万段の 1 本鎖になる。
            Zdd chain = manager.Base;
            for (int item = DeepVariableCount - 1; item >= 0; item--)
            {
                chain = manager.CreateNode(item, lo: manager.Empty, hi: chain);
            }

            int[] weights = new int[DeepVariableCount];
            Array.Fill(weights, 1);

            WeightedSet<int> best = chain.MaxWeight(weights);
            Assert.Equal(DeepVariableCount, best.Weight);
            Assert.Equal(DeepVariableCount, best.Items.Length);

            Assert.Equal(DeepVariableCount, chain.MinWeight(weights).Weight);
            Assert.Equal(DeepVariableCount, chain.TopK(weights, 3).Single().Weight);

            // 頻度は「唯一の集合に全 item が入っている」ので、どれも 1。
            Assert.All(chain.ItemFrequency(), value => Assert.Equal(1.0, value));
            Assert.Equal(
                (double)DeepVariableCount,
                chain.ExpectedValue(weights.Select(w => (double)w).ToArray()),
                Tolerance);

            // 確率は 0.5^100000 なので double では 0 に潰れる。落ちないことが要点。
            double[] half = new double[DeepVariableCount];
            Array.Fill(half, 0.5);
            Assert.Equal(0.0, chain.Probability(half));

            // 10 万段すべてで枝が分かれる族でも同じ。どの item も「入れても入れなくてもよい」ので
            // 最大は全体集合、最小は空集合、確率は 1。
            Zdd powerSet = PowerSetOf(manager);

            Assert.Equal(DeepVariableCount, powerSet.MaxWeight(weights).Weight);
            Assert.Equal(0, powerSet.MinWeight(weights).Weight);
            Assert.Equal(1.0, powerSet.Probability(half), Tolerance);
        }

        // ---- 補助 ----

        /// <summary>
        /// 最大・最小が総当たりと一致することを確かめる。同点があるので、照合するのは重みで、
        /// 返った集合については「族に属する」「重みが合っている」ことを見る。
        /// </summary>
        private static void AssertOptimizationMatchesNaive(
            ZddManager manager,
            BruteForceFamily family,
            int[] weights)
        {
            Zdd zdd = ZddFamilies.Build(manager, family);

            if (family.IsEmpty)
            {
                Assert.Throws<InvalidOperationException>(() => zdd.MaxWeight(weights));
                Assert.Throws<InvalidOperationException>(() => zdd.MinWeight(weights));
                return;
            }

            int expectedMax = family.Masks.Max(mask => WeightOf(mask, weights));
            int expectedMin = family.Masks.Min(mask => WeightOf(mask, weights));

            WeightedSet<int> best = zdd.MaxWeight(weights);
            WeightedSet<int> worst = zdd.MinWeight(weights);

            Assert.Equal(expectedMax, best.Weight);
            Assert.Equal(expectedMin, worst.Weight);

            foreach (WeightedSet<int> found in new[] { best, worst })
            {
                Assert.True(zdd.Contains(found.Items), $"{found} is not in {family}.");
                Assert.Equal(found.Weight, found.Items.Sum(item => weights[item]));

                // 昇順・重複なし。
                Assert.Equal(found.Items.Distinct().Order(), found.Items);
            }
        }

        /// <summary>境界の族とランダムな族を混ぜて返す。</summary>
        private static IEnumerable<BruteForceFamily> Families(int variableCount, int seed)
        {
            yield return BruteForceFamily.Base(variableCount);
            yield return BruteForceFamily.PowerSet(variableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(variableCount, 6, seed))
            {
                yield return family;
            }
        }

        /// <summary>素朴な確率計算: 族の集合 1 つずつについて、その集合が選ばれる確率を足す。</summary>
        private static double NaiveProbability(BruteForceFamily family, double[] probabilities)
        {
            double total = 0.0;

            foreach (int mask in family.Masks)
            {
                double probability = 1.0;

                for (int item = 0; item < probabilities.Length; item++)
                {
                    probability *= (mask & (1 << item)) != 0 ? probabilities[item] : 1.0 - probabilities[item];
                }

                total += probability;
            }

            return total;
        }

        private static int WeightOf(int mask, int[] weights)
        {
            int weight = 0;

            for (int item = 0; item < weights.Length; item++)
            {
                if ((mask & (1 << item)) != 0)
                {
                    weight += weights[item];
                }
            }

            return weight;
        }

        private static double WeightOf(int mask, double[] weights)
        {
            double weight = 0.0;

            for (int item = 0; item < weights.Length; item++)
            {
                if ((mask & (1 << item)) != 0)
                {
                    weight += weights[item];
                }
            }

            return weight;
        }

        private static Rational RationalWeightOf(int mask, Rational[] weights)
        {
            Rational weight = RationalWeightOps.Zero;

            for (int item = 0; item < weights.Length; item++)
            {
                if ((mask & (1 << item)) != 0)
                {
                    weight = RationalWeightOps.Add(weight, weights[item]);
                }
            }

            return weight;
        }

        /// <summary>負も含む小さな重み（総和が int に収まる範囲）。</summary>
        private static int[] RandomWeights(int variableCount, Random random)
        {
            int[] weights = new int[variableCount];

            for (int item = 0; item < variableCount; item++)
            {
                weights[item] = random.Next(-9, 10);
            }

            return weights;
        }

        private static double[] RandomProbabilities(int variableCount, Random random)
        {
            double[] probabilities = new double[variableCount];

            for (int item = 0; item < variableCount; item++)
            {
                probabilities[item] = random.NextDouble();
            }

            return probabilities;
        }

        /// <summary>全体集合の冪集合 2^U。どの item も「入れても入れなくてもよい」ノードを積む。</summary>
        private static Zdd PowerSetOf(ZddManager manager)
        {
            Zdd result = manager.Base;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                result = manager.CreateNode(item, result, result);
            }

            return result;
        }

        /// <summary>利用者が定義する重み型の例: 有理数（約分はしないので、比較で通分する）。</summary>
        private readonly record struct Rational(long Numerator, long Denominator);

        /// <summary>有理数の演算。<see cref="IWeightOps{TWeight}"/> の利用者定義実装。</summary>
        private readonly struct RationalWeightOps : IWeightOps<Rational>
        {
            public static Rational Zero => new Rational(0, 1);

            public static Rational Add(Rational left, Rational right) =>
                new Rational(
                    (left.Numerator * right.Denominator) + (right.Numerator * left.Denominator),
                    left.Denominator * right.Denominator);

            public static int Compare(Rational left, Rational right) =>
                (left.Numerator * right.Denominator).CompareTo(right.Numerator * left.Denominator);
        }

        /// <summary>利用者が定義する重み型の例: 主要素で比べ、同点なら副要素で比べる組。</summary>
        private readonly record struct Pair(int Primary, int Secondary);

        /// <summary>辞書順タプルの演算。<see cref="IWeightOps{TWeight}"/> の利用者定義実装。</summary>
        private readonly struct LexicographicWeightOps : IWeightOps<Pair>
        {
            public static Pair Zero => new Pair(0, 0);

            public static Pair Add(Pair left, Pair right) =>
                new Pair(left.Primary + right.Primary, left.Secondary + right.Secondary);

            public static int Compare(Pair left, Pair right)
            {
                int primary = left.Primary.CompareTo(right.Primary);

                return primary != 0 ? primary : left.Secondary.CompareTo(right.Secondary);
            }
        }
    }
}

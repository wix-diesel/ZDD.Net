using System;
using System.Linq;
using Xunit;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// 照合の土台になる素朴実装そのものの検証。
    /// </summary>
    /// <remarks>
    /// 照合相手が間違っていたら照合の意味がないので、
    /// (1) 手で計算できる小さな例、(2) 定義から出る代数法則、の 2 通りで確かめる。
    /// </remarks>
    public class BruteForceFamilyTests
    {
        // f = {∅, {0}, {1, 2}} / g = {{0}, {2}}（変数 4 個）。以下の期待値はすべて手計算。
        private static BruteForceFamily F => BruteForceFamily.FromSets(4, [], [0], [1, 2]);

        private static BruteForceFamily G => BruteForceFamily.FromSets(4, [0], [2]);

        // ---- 生成 ----

        [Fact]
        public void TheTerminalFamiliesAreWhatTheirNamesSay()
        {
            Assert.Empty(BruteForceFamily.Empty(3).Masks);
            Assert.True(BruteForceFamily.Empty(3).IsEmpty);

            Assert.Equal(new[] { 0 }, BruteForceFamily.Base(3).Masks);
            Assert.False(BruteForceFamily.Base(3).IsEmpty);

            Assert.Equal(new[] { 0b010 }, BruteForceFamily.Singleton(3, 1).Masks);

            Assert.Equal(8, BruteForceFamily.PowerSet(3).Count);
            Assert.Equal(0b111, BruteForceFamily.PowerSet(3).UniverseMask);
        }

        [Fact]
        public void SetsAndMasksDescribeTheSameFamily()
        {
            BruteForceFamily fromSets = BruteForceFamily.FromSets(4, [0, 2], [1], []);
            BruteForceFamily fromMasks = BruteForceFamily.FromMasks(4, [0b0101, 0b0010, 0b0000]);

            Assert.Equal(fromMasks, fromSets);
            Assert.Equal(new[] { 0b0000, 0b0010, 0b0101 }, fromSets.Masks);
            Assert.True(fromSets.ContainsSet(0, 2));
            Assert.False(fromSets.ContainsSet(0, 1));
        }

        [Fact]
        public void DuplicateSetsCollapseIntoOne()
        {
            Assert.Equal(1, BruteForceFamily.FromSets(3, [1, 0], [0, 1]).Count);
        }

        // ---- 集合演算（M1-7）----

        [Fact]
        public void TheSetOperationsMatchTheHandComputedAnswers()
        {
            Assert.Equal(BruteForceFamily.FromSets(4, [], [0], [2], [1, 2]), F.Union(G));
            Assert.Equal(BruteForceFamily.FromSets(4, [0]), F.Intersect(G));
            Assert.Equal(BruteForceFamily.FromSets(4, [], [1, 2]), F.Difference(G));
            Assert.Equal(BruteForceFamily.FromSets(4, [], [2], [1, 2]), F.SymmetricDifference(G));
        }

        // ---- 積・商・剰余（M1-8）----

        [Fact]
        public void TheProductIsEveryUnionOfAPair()
        {
            Assert.Equal(
                BruteForceFamily.FromSets(4, [0], [2], [0, 2], [1, 2], [0, 1, 2]),
                F.Product(G));
        }

        [Fact]
        public void TheQuotientAndRemainderMatchTheHandComputedAnswers()
        {
            // h = {{0, 1}, {0, 2}, {1}, {2}} を {{0}} で割ると {{1}, {2}}、余りも {{1}, {2}}。
            BruteForceFamily h = BruteForceFamily.FromSets(3, [0, 1], [0, 2], [1], [2]);
            BruteForceFamily divisor = BruteForceFamily.FromSets(3, [0]);

            Assert.Equal(BruteForceFamily.FromSets(3, [1], [2]), h.Quotient(divisor));
            Assert.Equal(BruteForceFamily.FromSets(3, [1], [2]), h.Remainder(divisor));

            // F を G で割ると、どちらの b とも重ならず和が F に入る a はないので空。
            Assert.True(F.Quotient(G).IsEmpty);
            Assert.Equal(F, F.Remainder(G));
        }

        [Fact]
        public void DividingByTheBaseFamilyChangesNothing()
        {
            Assert.Equal(F, F.Quotient(BruteForceFamily.Base(4)));
            Assert.True(F.Remainder(BruteForceFamily.Base(4)).IsEmpty);
        }

        [Fact]
        public void DividingByTheEmptyFamilyIsTheWholePowerSet()
        {
            // 「∀ b ∈ ∅」は空虚に真なので、定義どおりならすべての部分集合が商に入る。
            Assert.Equal(BruteForceFamily.PowerSet(4), F.Quotient(BruteForceFamily.Empty(4)));
            Assert.Equal(F, F.Remainder(BruteForceFamily.Empty(4)));
        }

        // ---- 包含系（M1-9）----

        [Fact]
        public void TheContainmentOperationsMatchTheHandComputedAnswers()
        {
            Assert.Equal(BruteForceFamily.FromSets(4, [], [0], [2]), F.Meet(G));
            Assert.Equal(BruteForceFamily.FromSets(4, [0], [1, 2]), F.Restrict(G));
            Assert.Equal(BruteForceFamily.FromSets(4, [], [0]), F.Permit(G));
            Assert.Equal(BruteForceFamily.FromSets(4, [1, 2]), F.NonSubsetsOf(G));
            Assert.Equal(BruteForceFamily.Base(4), F.NonSupersetsOf(G));
        }

        [Fact]
        public void RestrictAndPermitSplitTheFamilyWithTheirNegations()
        {
            foreach (BruteForceFamily f in FamilyCases.RandomFamilies(6, 20, seed: 20260830))
            {
                BruteForceFamily g = BruteForceFamily.Random(6, 0.05, seed: 4649 + f.Count);

                Assert.Equal(f, f.Restrict(g).Union(f.NonSupersetsOf(g)));
                Assert.True(f.Restrict(g).Intersect(f.NonSupersetsOf(g)).IsEmpty);

                Assert.Equal(f, f.Permit(g).Union(f.NonSubsetsOf(g)));
                Assert.True(f.Permit(g).Intersect(f.NonSubsetsOf(g)).IsEmpty);
            }
        }

        // ---- 極大・極小（M1-10）----

        [Fact]
        public void TheExtremalOperationsMatchTheHandComputedAnswers()
        {
            Assert.Equal(BruteForceFamily.FromSets(4, [0], [1, 2]), F.Maximal());
            Assert.Equal(BruteForceFamily.Base(4), F.Minimal());
        }

        [Fact]
        public void TheHittingSetsMeetEverySetOfTheFamily()
        {
            BruteForceFamily h = BruteForceFamily.FromSets(3, [0], [1, 2]);

            Assert.Equal(BruteForceFamily.FromSets(3, [0, 1], [0, 2], [0, 1, 2]), h.HittingSets());
            Assert.Equal(BruteForceFamily.FromSets(3, [0, 1], [0, 2]), h.HittingSets().Minimal());

            // ∅ を含む族はどの集合とも交われない。空の族は条件が空虚に真なので冪集合。
            Assert.True(BruteForceFamily.FromSets(3, [], [0]).HittingSets().IsEmpty);
            Assert.Equal(BruteForceFamily.PowerSet(3), BruteForceFamily.Empty(3).HittingSets());
        }

        [Fact]
        public void TheComplementIsThePowerSetMinusTheFamily()
        {
            BruteForceFamily f = BruteForceFamily.FromSets(2, [], [0]);

            Assert.Equal(BruteForceFamily.FromSets(2, [1], [0, 1]), f.Complement());
            Assert.Equal(f, f.Complement().Complement());
            Assert.Equal(BruteForceFamily.PowerSet(2), f.Union(f.Complement()));
        }

        // ---- 単項演算（M1-5）----

        [Fact]
        public void TheUnaryOperationsMatchTheHandComputedAnswers()
        {
            Assert.Equal(BruteForceFamily.FromSets(4, [0], [], [0, 1, 2]), F.Change(0));
            Assert.Equal(BruteForceFamily.Base(4), F.OnSet(0));
            Assert.Equal(BruteForceFamily.FromSets(4, [], [1, 2]), F.OffSet(0));

            Assert.Equal(BruteForceFamily.FromSets(4, [1], [0, 1], [2]), F.Change(1));
            Assert.Equal(BruteForceFamily.FromSets(4, [2]), F.OnSet(1));
            Assert.Equal(BruteForceFamily.FromSets(4, [], [0]), F.OffSet(1));
        }

        // ---- 定義から出る性質（ランダムな族で）----

        [Theory]
        [InlineData(3)]
        [InlineData(6)]
        [InlineData(10)]
        public void TheFamilyAlgebraObeysItsLaws(int variableCount)
        {
            BruteForceFamily[] families =
                FamilyCases.RandomFamilies(variableCount, 8, seed: 1729 + variableCount, density: 0.08).ToArray();

            foreach (BruteForceFamily f in families)
            {
                foreach (BruteForceFamily g in families)
                {
                    // 交換則
                    Assert.Equal(f.Union(g), g.Union(f));
                    Assert.Equal(f.Intersect(g), g.Intersect(f));
                    Assert.Equal(f.SymmetricDifference(g), g.SymmetricDifference(f));
                    Assert.Equal(f.Product(g), g.Product(f));
                    Assert.Equal(f.Meet(g), g.Meet(f));

                    // 差と対称差
                    Assert.Equal(f.Union(g).Difference(f.Intersect(g)), f.SymmetricDifference(g));

                    // 割り算の定義式 f = f/g * g + f%g
                    Assert.Equal(f, f.Quotient(g).Product(g).Union(f.Remainder(g)));

                    // 包含系は f の部分族
                    Assert.Equal(f.Restrict(g), f.Restrict(g).Intersect(f));
                    Assert.Equal(f.Permit(g), f.Permit(g).Intersect(f));

                    foreach (BruteForceFamily h in families.Take(3))
                    {
                        // 結合則
                        Assert.Equal(f.Union(g).Union(h), f.Union(g.Union(h)));
                        Assert.Equal(f.Intersect(g).Intersect(h), f.Intersect(g.Intersect(h)));
                        Assert.Equal(f.Product(g).Product(h), f.Product(g.Product(h)));

                        // 分配則（積は和の上に分配する）
                        Assert.Equal(f.Product(g.Union(h)), f.Product(g).Union(f.Product(h)));
                    }
                }
            }
        }

        [Theory]
        [InlineData(3)]
        [InlineData(8)]
        public void TheExtremalAndHittingOperationsObeyTheirDefinitions(int variableCount)
        {
            foreach (BruteForceFamily f in FamilyCases.RandomFamilies(variableCount, 20, seed: 31337 + variableCount))
            {
                BruteForceFamily maximal = f.Maximal();
                BruteForceFamily minimal = f.Minimal();

                // 極大・極小は f の部分族で、べき等。
                Assert.Equal(maximal, maximal.Intersect(f));
                Assert.Equal(minimal, minimal.Intersect(f));
                Assert.Equal(maximal, maximal.Maximal());
                Assert.Equal(minimal, minimal.Minimal());

                // f のどの集合も、極大な集合に含まれ、極小な集合を含む。
                Assert.Equal(f, f.Permit(maximal));
                Assert.Equal(f, f.Restrict(minimal));

                if (variableCount <= BruteForceFamily.MaxPowerSetVariableCount)
                {
                    // ヒッティング集合は上に閉じている（大きくしても交わりは消えない）。
                    BruteForceFamily hitting = f.HittingSets();
                    BruteForceFamily powerSet = BruteForceFamily.PowerSet(variableCount);

                    Assert.Equal(hitting, hitting.Product(powerSet));

                    // 極小なヒッティング集合を大きくしたものが、ちょうど全ヒッティング集合。
                    Assert.Equal(hitting, hitting.Minimal().Product(powerSet));
                }
            }
        }

        // ---- 再現性 ----

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(10)]
        [InlineData(FamilyCases.ExhaustiveVariableLimit)]
        public void TheDefaultDensityDrawsFamiliesOfAUsefulSize(int variableCount)
        {
            // 1 つ 1 つは空にも冪集合にもなり得る（それも照合したい入力）。
            // 見たいのは、20 個引けば「空でも冪集合でもない族」がちゃんと混ざること。
            BruteForceFamily[] families = FamilyCases.RandomFamilies(variableCount, 20, seed: 555).ToArray();
            int subsetCount = 1 << variableCount;

            Assert.Contains(families, family => !family.IsEmpty && family.Count < subsetCount);

            double average = families.Average(family => family.Count);

            Assert.InRange(average, 0.5, subsetCount * 0.75);
        }

        [Fact]
        public void TheSameSeedProducesTheSameFamily()
        {
            BruteForceFamily first = BruteForceFamily.Random(10, 0.05, seed: 20260830);
            BruteForceFamily second = BruteForceFamily.Random(10, 0.05, seed: 20260830);

            Assert.Equal(first, second);
            Assert.Equal(first.Masks, second.Masks);

            // 同じ乱数源から続けて引いた並びも、シードが同じなら丸ごと一致する。
            Assert.Equal(
                FamilyCases.RandomFamilies(8, 5, seed: 4649).ToArray(),
                FamilyCases.RandomFamilies(8, 5, seed: 4649).ToArray());

            Assert.Equal(
                FamilyCases.RandomFamiliesOfSets(20, 5, seed: 4649, setCount: 30).ToArray(),
                FamilyCases.RandomFamiliesOfSets(20, 5, seed: 4649, setCount: 30).ToArray());
        }

        [Fact]
        public void ADifferentSeedProducesADifferentFamily()
        {
            Assert.NotEqual(
                BruteForceFamily.Random(10, 0.05, seed: 20260830),
                BruteForceFamily.Random(10, 0.05, seed: 20260831));
        }

        [Fact]
        public void TheDensityControlsHowManySetsAreDrawn()
        {
            Assert.True(BruteForceFamily.Random(8, 0.0, seed: 1).IsEmpty);
            Assert.Equal(BruteForceFamily.PowerSet(8), BruteForceFamily.Random(8, 1.0, seed: 1));

            int sparse = BruteForceFamily.Random(10, 0.01, seed: 7).Count;
            int dense = BruteForceFamily.Random(10, 0.5, seed: 7).Count;

            Assert.True(sparse < dense, $"a density of 0.01 drew {sparse} set(s) and 0.5 drew {dense}.");
        }

        [Fact]
        public void RandomSetsDrawsAtMostTheRequestedNumberOfSets()
        {
            BruteForceFamily family = BruteForceFamily.RandomSets(24, 40, new Random(99));

            Assert.True(family.Count <= 40, $"drew {family.Count} set(s).");
            Assert.All(family.Masks, mask => Assert.Equal(0, mask & ~family.UniverseMask));
        }

        // ---- 表示 ----

        [Fact]
        public void AFamilyPrintsItsSets()
        {
            Assert.Equal("{∅, {0}, {1, 2}}", F.ToString());
            Assert.Equal("{} (empty family)", BruteForceFamily.Empty(4).ToString());
            Assert.Equal("{∅, {0}, … (+1 more)}", F.Describe(2));
            Assert.Equal("∅", BruteForceFamily.FormatSet(0));
            Assert.Equal("{0, 3}", BruteForceFamily.FormatSet(0b1001));
        }

        // ---- 引数の検査 ----

        [Fact]
        public void FamiliesOfDifferentVariableCountsCannotBeCombined()
        {
            BruteForceFamily small = BruteForceFamily.Base(2);
            BruteForceFamily large = BruteForceFamily.Base(3);

            Assert.Equal("other", Assert.Throws<ArgumentException>(() => small.Union(large)).ParamName);
            Assert.Equal("other", Assert.Throws<ArgumentException>(() => small.Product(large)).ParamName);
            Assert.Equal("other", Assert.Throws<ArgumentException>(() => small.Meet(large)).ParamName);
            Assert.Equal("other", Assert.Throws<ArgumentException>(() => small.Quotient(large)).ParamName);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        public void AnItemOutsideTheFamilyIsRejected(int item)
        {
            BruteForceFamily family = BruteForceFamily.Base(3);

            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => family.Change(item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => family.OnSet(item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => family.OffSet(item)).ParamName);
        }

        [Fact]
        public void AMaskOutsideTheUniverseIsRejected()
        {
            Assert.Equal(
                "masks",
                Assert.Throws<ArgumentOutOfRangeException>(() => BruteForceFamily.FromMasks(2, [0b100])).ParamName);
        }

        [Fact]
        public void TooManyVariablesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BruteForceFamily.Empty(BruteForceFamily.MaxVariableCount + 1));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => BruteForceFamily.PowerSet(BruteForceFamily.MaxPowerSetVariableCount + 1));

            // 冪集合を歩く演算だけは、族が大きすぎると断る（残りの演算は素通りする）。
            BruteForceFamily wide = BruteForceFamily.RandomSets(
                BruteForceFamily.MaxPowerSetVariableCount + 1,
                4,
                new Random(1));

            Assert.Throws<InvalidOperationException>(() => wide.Complement());
            Assert.Throws<InvalidOperationException>(() => wide.HittingSets());
            Assert.Equal(wide, wide.Union(wide));
        }
    }
}

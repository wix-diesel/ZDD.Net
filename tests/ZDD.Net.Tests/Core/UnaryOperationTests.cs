using System;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="Zdd.Change"/> / <see cref="Zdd.OnSet"/> / <see cref="Zdd.OffSet"/> の検証。
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="BruteForceFamily"/>（定義をそのままループで書いた素朴実装）で、
    /// 比較は <see cref="FamilyAssert.AssertSameFamily(string?, in Zdd, BruteForceFamily)"/> が行う。
    /// 総当たりの回し方は <see cref="FamilyCases"/> にある。
    /// </remarks>
    public class UnaryOperationTests
    {
        // ---- 総当たり照合 ----

        [Fact]
        public void EveryFamilyOfThreeVariablesMatchesTheNaiveImplementation()
        {
            const int VariableCount = 3;

            using ZddManager manager = new ZddManager(VariableCount);

            // 3 変数の集合は 8 個。その部分集合＝族は 2^8 = 256 通りで、すべて試せる。
            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertUnaryOperationsMatchNaive(manager, family);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(FamilyCases.DefaultVariableCount)]
        [InlineData(FamilyCases.ExhaustiveVariableLimit)]
        public void RandomFamiliesMatchTheNaiveImplementation(int variableCount)
        {
            using ZddManager manager = new ZddManager(variableCount);

            foreach (BruteForceFamily family in
                FamilyCases.RandomFamilies(variableCount, 50, seed: 20260830 + variableCount))
            {
                AssertUnaryOperationsMatchNaive(manager, family);
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void EverySubsetOfTwelveVariablesMatchesTheNaiveImplementation()
        {
            const int VariableCount = FamilyCases.ExhaustiveVariableLimit;

            using ZddManager manager = new ZddManager(VariableCount);

            // 1 つの集合だけを持つ族を、2^12 = 4096 個の集合すべてについて回す。
            foreach (int mask in FamilyCases.AllSubsets(VariableCount))
            {
                AssertUnaryOperationsMatchNaive(manager, BruteForceFamily.FromMasks(VariableCount, [mask]));
            }

            // 4096 個すべてを持つ族（冪集合）も 1 度。
            AssertUnaryOperationsMatchNaive(manager, BruteForceFamily.PowerSet(VariableCount));
        }

        // ---- 代数的な性質 ----

        [Fact]
        public void ChangeAppliedTwiceIsTheIdentity()
        {
            const int VariableCount = 8;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 50, seed: 4649))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                for (int item = 0; item < VariableCount; item++)
                {
                    Assert.Equal(zdd, zdd.Change(item).Change(item));
                }
            }
        }

        [Fact]
        public void OnSetAndOffSetSplitTheFamilyInTwo()
        {
            const int VariableCount = 8;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 50, seed: 1729))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                for (int item = 0; item < VariableCount; item++)
                {
                    // 和は演算としてはまだ無い（M1-7）ので、素朴表現の側で合わせる。
                    BruteForceFamily without = ZddFamilies.ToBruteForce(zdd.OffSet(item));
                    BruteForceFamily with = ZddFamilies.ToBruteForce(zdd.OnSet(item).Change(item));

                    Assert.True(without.Intersect(with).IsEmpty);
                    Assert.Equal(family, without.Union(with));
                }
            }
        }

        [Fact]
        public void OnSetAndOffSetOfTheTerminalsAreTheExpectedTerminals()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd empty = manager.Empty;
            Zdd @base = manager.Base;

            Assert.Equal(empty, empty.Change(2));
            Assert.Equal(empty, empty.OnSet(2));
            Assert.Equal(empty, empty.OffSet(2));

            // {∅} の item を反転すると {{item}}、すなわち Singleton になる。
            Assert.Equal(manager.Singleton(2), @base.Change(2));
            Assert.Equal(empty, @base.OnSet(2));
            Assert.Equal(@base, @base.OffSet(2));

            Assert.Equal(@base, manager.Singleton(2).OnSet(2));
            Assert.Equal(empty, manager.Singleton(2).OffSet(2));
        }

        [Fact]
        public void TheAliasesAreTheSameOperations()
        {
            using ZddManager manager = new ZddManager(6);

            Zdd zdd = ZddFamilies.Build(manager, [0], [1, 4], [0, 1, 4], [3, 5]);

            for (int item = 0; item < 6; item++)
            {
                Assert.Equal(zdd.OnSet(item), zdd.Subset1(item));
                Assert.Equal(zdd.OffSet(item), zdd.Subset0(item));
            }
        }

        // ---- キャッシュ ----

        [Fact]
        public void TheResultIsTheSameWithAndWithoutTheOperationCache()
        {
            const int VariableCount = FamilyCases.DefaultVariableCount;

            ZddManagerOptions disabled = new ZddManagerOptions { InitialCacheCapacity = 0, MaxCacheCapacity = 0 };

            using ZddManager cached = new ZddManager(VariableCount);
            using ZddManager uncached = new ZddManager(VariableCount, disabled);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 30, seed: 31337))
            {
                Zdd withCache = ZddFamilies.Build(cached, family);
                Zdd withoutCache = ZddFamilies.Build(uncached, family);

                for (int item = 0; item < VariableCount; item++)
                {
                    AssertUnaryOperationsMatchNaive(cached, withCache, family, item);
                    AssertUnaryOperationsMatchNaive(uncached, withoutCache, family, item);
                }
            }
        }

        [Fact]
        public void SharedNodesAreVisitedOnceEvenWithoutTheOperationCache()
        {
            // 各変数が「入っていても入っていなくてもよい」鎖（＝冪集合）。パスは 2^64 本あるが、
            // ノードは 64 個しかない。途中結果表が効いていなければ、この呼び出しは終わらない。
            const int VariableCount = 64;

            ZddManagerOptions disabled = new ZddManagerOptions { InitialCacheCapacity = 0, MaxCacheCapacity = 0 };
            using ZddManager manager = new ZddManager(VariableCount, disabled);

            Zdd powerSet = manager.Base;
            for (int item = VariableCount - 1; item >= 0; item--)
            {
                powerSet = manager.CreateNode(item, powerSet, powerSet);
            }

            Assert.Equal((long)VariableCount, powerSet.NodeCount);

            // 冪集合は「item を含む／含まない」で対称なので、反転しても変わらない。
            Assert.Equal(powerSet, powerSet.Change(VariableCount - 1));

            // item を含む集合から item を除いたものは、残りの変数の冪集合（ノードが 1 つ減る）。
            Assert.Equal((long)(VariableCount - 1), powerSet.OnSet(VariableCount - 1).NodeCount);
            Assert.Equal((long)(VariableCount - 1), powerSet.OffSet(VariableCount - 1).NodeCount);
        }

        // ---- 深い ZDD（スタックオーバーフロー回帰テスト） ----

        [Fact]
        public void DeepDiagramsDoNotOverflowTheStack()
        {
            // 変数 10 万の鎖。素直な再帰実装ならここで StackOverflowException になり、
            // .NET では catch できずプロセスごと落ちる（docs/PLAN.md §4.5）。
            const int VariableCount = 100_000;
            int deepest = VariableCount - 1;

            using ZddManager manager = new ZddManager(VariableCount);

            // { {i} : 0 <= i < VariableCount }。深さ・ノード数ともに VariableCount。
            Zdd singletons = manager.Empty;
            for (int item = deepest; item >= 0; item--)
            {
                singletons = manager.CreateNode(item, singletons, manager.Base);
            }

            Assert.Equal((long)VariableCount, singletons.NodeCount);

            // 最も深い item を対象にすると、根から葉まで全段を降りることになる。
            // Change: {deepest} は ∅ に、他の {i} は {i, deepest} になる。
            // 出来上がるのは「item ごとの節 (deepest 個) ＋ 共有される {{deepest}} の節 1 個」。
            Zdd changed = singletons.Change(deepest);
            Assert.Equal((long)VariableCount, changed.NodeCount);

            // OnSet: deepest を含む集合は {deepest} だけ。そこから deepest を除くと ∅。
            Assert.True(singletons.OnSet(deepest).IsBase);

            // OffSet: {deepest} だけが消える。
            Assert.Equal((long)(VariableCount - 1), singletons.OffSet(deepest).NodeCount);

            // 反転を 2 回かけても元に戻る（深いまま）。
            Assert.Equal(singletons, changed.Change(deepest));
        }

        // ---- 引数の検査 ----

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        [InlineData(int.MaxValue)]
        public void AnItemOutsideTheManagerIsRejected(int item)
        {
            using ZddManager manager = new ZddManager(4);

            Zdd zdd = manager.Singleton(0);

            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.Change(item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.OnSet(item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.OffSet(item)).ParamName);
        }

        [Fact]
        public void AFamilyFromAnotherManagerIsRejected()
        {
            using ZddManager one = new ZddManager(4);
            using ZddManager other = new ZddManager(4);

            Zdd foreign = other.Singleton(0);

            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Change(foreign, 1)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.OnSet(foreign, 1)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.OffSet(foreign, 1)).ParamName);
        }

        [Fact]
        public void ADefaultHandleHasNoOperations()
        {
            Zdd none = default;

            Assert.Throws<InvalidOperationException>(() => none.Change(0));
            Assert.Throws<InvalidOperationException>(() => none.OnSet(0));
            Assert.Throws<InvalidOperationException>(() => none.OffSet(0));
        }

        [Fact]
        public void OperationsOnADisposedManagerThrow()
        {
            ZddManager manager = new ZddManager(4);
            Zdd zdd = manager.Singleton(1);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => zdd.Change(1));
            Assert.Throws<ObjectDisposedException>(() => zdd.OnSet(1));
            Assert.Throws<ObjectDisposedException>(() => zdd.OffSet(1));
        }

        // ---- 照合の本体 ----

        /// <summary>族を ZDD に組み立て、全 item について 3 つの単項演算を素朴実装と突き合わせる。</summary>
        private static void AssertUnaryOperationsMatchNaive(ZddManager manager, BruteForceFamily family)
        {
            Zdd zdd = ZddFamilies.Build(manager, family);

            // 組み立て自体が壊れていたら、以降の照合は何も言っていないことになる。
            FamilyAssert.AssertSameFamily("the family builder", zdd, family);

            for (int item = 0; item < manager.VariableCount; item++)
            {
                AssertUnaryOperationsMatchNaive(manager, zdd, family, item);
            }
        }

        private static void AssertUnaryOperationsMatchNaive(
            ZddManager manager,
            in Zdd zdd,
            BruteForceFamily family,
            int item)
        {
            FamilyAssert.AssertSameFamily($"Change({item})", zdd.Change(item), family.Change(item), family);
            FamilyAssert.AssertSameFamily($"OnSet({item})", zdd.OnSet(item), family.OnSet(item), family);
            FamilyAssert.AssertSameFamily($"OffSet({item})", zdd.OffSet(item), family.OffSet(item), family);
        }
    }
}

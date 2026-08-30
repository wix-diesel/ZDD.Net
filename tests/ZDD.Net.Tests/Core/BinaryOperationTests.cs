using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="Zdd.Union"/> / <see cref="Zdd.Intersect"/> / <see cref="Zdd.Difference"/> /
    /// <see cref="Zdd.SymmetricDifference"/> の検証。
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="BruteForceFamily"/>（定義をそのままループで書いた素朴実装）で、
    /// 比較は <see cref="FamilyAssert.AssertSameFamily(string?, in Zdd, BruteForceFamily, BruteForceFamily?)"/>
    /// が行う。総当たりの回し方は <see cref="FamilyCases"/> にある。
    /// </remarks>
    public class BinaryOperationTests
    {
        // ---- 総当たり照合 ----

        [Fact]
        public void EveryPairOfFamiliesOfTwoVariablesMatchesTheNaiveImplementation()
        {
            const int VariableCount = 2;

            using ZddManager manager = new ZddManager(VariableCount);

            // 2 変数の族は 2^(2^2) = 16 通り。その対 256 通りをすべて試す。
            BruteForceFamily[] families = FamilyCases.AllFamilies(VariableCount).ToArray();

            foreach (BruteForceFamily f in families)
            {
                foreach (BruteForceFamily g in families)
                {
                    AssertOperationsMatchNaive(manager, f, g);
                }
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void EveryPairOfFamiliesOfThreeVariablesMatchesTheNaiveImplementation()
        {
            const int VariableCount = 3;

            using ZddManager manager = new ZddManager(VariableCount);

            // 3 変数の族は 256 通り。その対 65536 通りをすべて試す。
            BruteForceFamily[] families = FamilyCases.AllFamilies(VariableCount).ToArray();

            foreach (BruteForceFamily f in families)
            {
                foreach (BruteForceFamily g in families)
                {
                    AssertOperationsMatchNaive(manager, f, g);
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(FamilyCases.DefaultVariableCount)]
        [InlineData(FamilyCases.ExhaustiveVariableLimit)]
        public void RandomPairsMatchTheNaiveImplementation(int variableCount)
        {
            using ZddManager manager = new ZddManager(variableCount);

            BruteForceFamily[] families =
                FamilyCases.RandomFamilies(variableCount, 24, seed: 20260830 + variableCount).ToArray();

            foreach (BruteForceFamily f in families)
            {
                foreach (BruteForceFamily g in families)
                {
                    AssertOperationsMatchNaive(manager, f, g);
                }
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void EverySubsetOfTwelveVariablesMatchesTheNaiveImplementation()
        {
            const int VariableCount = FamilyCases.ExhaustiveVariableLimit;

            using ZddManager manager = new ZddManager(VariableCount);

            // 冪集合を相手に、1 つの集合だけを持つ族を 2^12 = 4096 個すべて突き合わせる。
            // 「上のレベルにしか現れない変数」の扱いが、全パターン通ることになる。
            BruteForceFamily powerSet = BruteForceFamily.PowerSet(VariableCount);
            Zdd powerSetZdd = ZddFamilies.Build(manager, powerSet);

            foreach (int mask in FamilyCases.AllSubsets(VariableCount))
            {
                BruteForceFamily single = BruteForceFamily.FromMasks(VariableCount, [mask]);
                AssertOperationsMatchNaive(ZddFamilies.Build(manager, single), powerSetZdd, single, powerSet);
            }
        }

        // ---- 代数法則 ----

        [Fact]
        public void UnionAndIntersectAreCommutative()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 11))
            {
                Assert.Equal(f | g, g | f);
                Assert.Equal(f & g, g & f);
                Assert.Equal(f ^ g, g ^ f);
            }
        }

        [Fact]
        public void UnionAndIntersectAreAssociative()
        {
            foreach ((Zdd f, Zdd g, Zdd h) in Triples(seed: 22))
            {
                Assert.Equal((f | g) | h, f | (g | h));
                Assert.Equal((f & g) & h, f & (g & h));
                Assert.Equal((f ^ g) ^ h, f ^ (g ^ h));
            }
        }

        [Fact]
        public void UnionAndIntersectDistributeOverEachOther()
        {
            foreach ((Zdd f, Zdd g, Zdd h) in Triples(seed: 33))
            {
                Assert.Equal(f & (g | h), (f & g) | (f & h));
                Assert.Equal(f | (g & h), (f | g) & (f | h));
            }
        }

        [Fact]
        public void TheAbsorptionAndIdempotentLawsHold()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 44))
            {
                Assert.Equal(f, f | (f & g));
                Assert.Equal(f, f & (f | g));
                Assert.Equal(f, f | f);
                Assert.Equal(f, f & f);
            }
        }

        [Fact]
        public void TheSymmetricDifferenceIsTheUnionWithoutTheIntersection()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 55))
            {
                Assert.Equal(f ^ g, (f | g) - (f & g));
                Assert.Equal(f ^ g, (f - g) | (g - f));
            }
        }

        // ド・モルガン則（~(f|g) == ~f & ~g）は Complement が入る M1-10 で追加する。
        // 補は「全体集合の冪集合との差」なので、この PR の Difference が土台になる。

        [Fact]
        public void TheEmptyFamilyIsTheIdentityOfUnionAndTheZeroOfIntersect()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd empty = manager.Empty;

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 40, seed: 66))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                Assert.Equal(f, f | empty);
                Assert.Equal(f, empty | f);
                Assert.Equal(empty, f & empty);
                Assert.Equal(empty, empty & f);
                Assert.Equal(f, f - empty);
                Assert.Equal(empty, empty - f);
                Assert.Equal(empty, f - f);
                Assert.Equal(f, f ^ empty);
                Assert.Equal(empty, f ^ f);
            }
        }

        [Fact]
        public void TheTerminalsCombineAsExpected()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd empty = manager.Empty;
            Zdd @base = manager.Base;

            Assert.Equal(@base, empty | @base);
            Assert.Equal(empty, empty & @base);
            Assert.Equal(empty, empty - @base);
            Assert.Equal(@base, empty ^ @base);

            Assert.Equal(@base, @base | @base);
            Assert.Equal(@base, @base & @base);
            Assert.Equal(empty, @base - @base);
            Assert.Equal(empty, @base ^ @base);

            // {∅} は単位元ではない。{{0}} に足すと {∅, {0}} になる。
            Zdd singleton = manager.Singleton(0);
            FamilyAssert.AssertSameFamily(
                "{{0}} | {∅}",
                singleton | @base,
                BruteForceFamily.FromMasks(4, [0, 1]));
        }

        [Fact]
        public void TheOperatorsAreTheSameOperations()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 77))
            {
                Assert.Equal(f.Union(g), f | g);
                Assert.Equal(f.Intersect(g), f & g);
                Assert.Equal(f.Difference(g), f - g);
                Assert.Equal(f.SymmetricDifference(g), f ^ g);
            }
        }

        [Fact]
        public void OnSetAndOffSetSplitTheFamilyInTwo()
        {
            const int VariableCount = 8;

            using ZddManager manager = new ZddManager(VariableCount);

            // M1-5 では素朴表現の側で合わせていた分割の確認を、ZDD の演算だけで書き直す。
            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 30, seed: 88))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                for (int item = 0; item < VariableCount; item++)
                {
                    Zdd without = zdd.OffSet(item);
                    Zdd with = zdd.OnSet(item).Change(item);

                    Assert.True((without & with).IsEmpty);
                    Assert.Equal(zdd, without | with);
                }
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

            BruteForceFamily[] families =
                FamilyCases.RandomFamilies(VariableCount, 12, seed: 31337).ToArray();

            foreach (BruteForceFamily f in families)
            {
                foreach (BruteForceFamily g in families)
                {
                    AssertOperationsMatchNaive(cached, f, g);
                    AssertOperationsMatchNaive(uncached, f, g);
                }
            }
        }

        [Fact]
        public void SharedSubproblemsAreVisitedOnceEvenWithoutTheOperationCache()
        {
            // 「要素数が偶数の部分集合」と「奇数の部分集合」。どちらも段ごとに 2 状態しか無いので
            // ノードは高々 2n 個だが、パスは合わせて 2^n 本ある。
            // 途中結果表が効いていなければ、以下の演算は終わらない。
            const int VariableCount = 64;

            ZddManagerOptions disabled = new ZddManagerOptions { InitialCacheCapacity = 0, MaxCacheCapacity = 0 };
            using ZddManager manager = new ZddManager(VariableCount, disabled);

            Zdd even = manager.Base;
            Zdd odd = manager.Empty;

            for (int item = VariableCount - 1; item >= 0; item--)
            {
                // item を採ると偶奇が入れ替わる。
                (even, odd) = (manager.CreateNode(item, even, odd), manager.CreateNode(item, odd, even));
            }

            Zdd powerSet = manager.Base;
            for (int item = VariableCount - 1; item >= 0; item--)
            {
                powerSet = manager.CreateNode(item, powerSet, powerSet);
            }

            Assert.Equal(powerSet, even | odd);
            Assert.Equal(powerSet, even ^ odd);
            Assert.True((even & odd).IsEmpty);
            Assert.Equal(odd, powerSet - even);
            Assert.Equal(even, powerSet - odd);
            Assert.Equal(even, powerSet & even);
        }

        // ---- 深い ZDD（スタックオーバーフロー回帰テスト） ----

        [Fact]
        public void DeepDiagramsDoNotOverflowTheStack()
        {
            // 変数 10 万。素直な再帰実装ならここで StackOverflowException になり、
            // .NET では catch できずプロセスごと落ちる（docs/PLAN.md §4.5）。
            const int VariableCount = 100_000;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd all = BuildSingletons(manager, item => true);
            Zdd evens = BuildSingletons(manager, item => item % 2 == 0);
            Zdd odds = BuildSingletons(manager, item => item % 2 != 0);

            Assert.Equal((long)VariableCount, all.NodeCount);

            Assert.Equal(all, evens | odds);
            Assert.Equal(all, evens ^ odds);
            Assert.True((evens & odds).IsEmpty);
            Assert.Equal(evens, all & evens);
            Assert.Equal(odds, all - evens);
            Assert.True((all - all).IsEmpty);
        }

        // ---- 引数の検査 ----

        [Fact]
        public void AFamilyFromAnotherManagerIsRejected()
        {
            using ZddManager one = new ZddManager(4);
            using ZddManager other = new ZddManager(4);

            Zdd native = one.Singleton(0);
            Zdd foreign = other.Singleton(0);

            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.Union(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.Intersect(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.Difference(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.SymmetricDifference(foreign)).ParamName);

            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Union(foreign, native)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Intersect(foreign, native)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Difference(foreign, native)).ParamName);
            Assert.Equal(
                "f",
                Assert.Throws<ArgumentException>(() => one.SymmetricDifference(foreign, native)).ParamName);
        }

        [Fact]
        public void ADefaultHandleHasNoOperations()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd none = default;
            Zdd zdd = manager.Singleton(0);

            // 左辺が default なら、そもそも所有マネージャが分からない。
            Assert.Throws<InvalidOperationException>(() => none | zdd);
            Assert.Throws<InvalidOperationException>(() => none & zdd);
            Assert.Throws<InvalidOperationException>(() => none - zdd);
            Assert.Throws<InvalidOperationException>(() => none ^ zdd);

            // 右辺が default なら、どのマネージャにも属さない族を混ぜた誤用として弾く。
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd | none).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd & none).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd - none).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd ^ none).ParamName);
        }

        [Fact]
        public void OperationsOnADisposedManagerThrow()
        {
            ZddManager manager = new ZddManager(4);
            Zdd f = manager.Singleton(1);
            Zdd g = manager.Singleton(2);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => f | g);
            Assert.Throws<ObjectDisposedException>(() => f & g);
            Assert.Throws<ObjectDisposedException>(() => f - g);
            Assert.Throws<ObjectDisposedException>(() => f ^ g);
        }

        // ---- 照合の本体 ----

        private static void AssertOperationsMatchNaive(
            ZddManager manager,
            BruteForceFamily f,
            BruteForceFamily g) =>
            AssertOperationsMatchNaive(
                ZddFamilies.Build(manager, f),
                ZddFamilies.Build(manager, g),
                f,
                g);

        private static void AssertOperationsMatchNaive(
            in Zdd left,
            in Zdd right,
            BruteForceFamily f,
            BruteForceFamily g)
        {
            FamilyAssert.AssertSameFamily("f | g", left.Union(right), f.Union(g), g);
            FamilyAssert.AssertSameFamily("f & g", left.Intersect(right), f.Intersect(g), g);
            FamilyAssert.AssertSameFamily("f - g", left.Difference(right), f.Difference(g), g);
            FamilyAssert.AssertSameFamily(
                "f ^ g",
                left.SymmetricDifference(right),
                f.SymmetricDifference(g),
                g);
        }

        // ---- 代数法則で使う族の作り置き ----

        /// <summary>代数法則の検証に使う族の対を返す。マネージャは呼び出しごとに使い捨てる。</summary>
        private static IEnumerable<(Zdd F, Zdd G)> Pairs(int seed)
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd[] families = FamilyCases.RandomFamilies(VariableCount, 12, seed)
                .Select(family => ZddFamilies.Build(manager, family))
                .ToArray();

            foreach (Zdd f in families)
            {
                foreach (Zdd g in families)
                {
                    yield return (f, g);
                }
            }
        }

        /// <summary>代数法則の検証に使う族の三つ組を返す。</summary>
        private static IEnumerable<(Zdd F, Zdd G, Zdd H)> Triples(int seed)
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd[] families = FamilyCases.RandomFamilies(VariableCount, 8, seed)
                .Select(family => ZddFamilies.Build(manager, family))
                .ToArray();

            foreach (Zdd f in families)
            {
                foreach (Zdd g in families)
                {
                    foreach (Zdd h in families)
                    {
                        yield return (f, g, h);
                    }
                }
            }
        }

        /// <summary>
        /// <c>{ {i} : include(i) }</c>（1 要素集合だけの族）を、根から葉まで 1 段ずつ積んで作る。
        /// </summary>
        private static Zdd BuildSingletons(ZddManager manager, Func<int, bool> include)
        {
            Zdd result = manager.Empty;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                result = manager.CreateNode(item, result, include(item) ? manager.Base : manager.Empty);
            }

            return result;
        }
    }
}

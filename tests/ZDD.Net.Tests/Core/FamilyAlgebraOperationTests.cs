using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="Zdd.Product"/> / <see cref="Zdd.Quotient"/> / <see cref="Zdd.Remainder"/> の検証。
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="BruteForceFamily"/>（定義をそのままループで書いた素朴実装）で、
    /// 比較は <see cref="FamilyAssert.AssertSameFamily(string?, in Zdd, BruteForceFamily, BruteForceFamily?)"/>
    /// が行う。総当たりの回し方は <see cref="FamilyCases"/> にある。
    /// </remarks>
    public class FamilyAlgebraOperationTests
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
                FamilyCases.RandomFamilies(variableCount, 10, seed: 20260830 + variableCount).ToArray();

            foreach (BruteForceFamily f in families)
            {
                foreach (BruteForceFamily g in families)
                {
                    AssertOperationsMatchNaive(manager, f, g);
                }
            }
        }

        [Fact]
        public void EverySingleSetFamilyAgainstThePowerSetMatchesTheNaiveImplementation()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            // 冪集合を相手に、1 つの集合だけを持つ族を 2^6 = 64 個すべて突き合わせる。
            // 「上のレベルにしか現れない変数」と「割り切れる／割り切れない」の両方が全パターン通る。
            BruteForceFamily powerSet = BruteForceFamily.PowerSet(VariableCount);
            Zdd powerSetZdd = ZddFamilies.Build(manager, powerSet);

            foreach (int mask in FamilyCases.AllSubsets(VariableCount))
            {
                BruteForceFamily single = BruteForceFamily.FromMasks(VariableCount, [mask]);
                Zdd singleZdd = ZddFamilies.Build(manager, single);

                AssertOperationsMatchNaive(singleZdd, powerSetZdd, single, powerSet);
                AssertOperationsMatchNaive(powerSetZdd, singleZdd, powerSet, single);
            }
        }

        // ---- 割り算の定義式 ----

        [Fact]
        public void TheDivisionIdentityHolds()
        {
            // f == f/g * g + f%g（+ は Union）。剰余の定義そのものだが、
            // 商・積・差のどれか 1 つが崩れれば破れるので、3 演算の噛み合わせを見張る式になる。
            foreach ((Zdd f, Zdd g) in Pairs(seed: 101))
            {
                Assert.Equal(f, (f / g * g) | (f % g));
            }
        }

        [Fact]
        public void TheQuotientTimesTheDivisorIsPartOfTheDividend()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 102))
            {
                // くくり出せたぶんは必ず f の部分族で、余りとは重ならない。
                Zdd divisible = f / g * g;

                Assert.Equal(divisible, divisible & f);
                Assert.True((divisible & (f % g)).IsEmpty);
            }
        }

        // ---- 積の代数法則 ----

        [Fact]
        public void ProductIsCommutative()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 111))
            {
                Assert.Equal(f * g, g * f);
            }
        }

        [Fact]
        public void ProductIsAssociative()
        {
            foreach ((Zdd f, Zdd g, Zdd h) in Triples(seed: 222))
            {
                Assert.Equal((f * g) * h, f * (g * h));
            }
        }

        [Fact]
        public void ProductDistributesOverUnion()
        {
            foreach ((Zdd f, Zdd g, Zdd h) in Triples(seed: 333))
            {
                Assert.Equal(f * (g | h), (f * g) | (f * h));
                Assert.Equal((g | h) * f, (g * f) | (h * f));
            }
        }

        [Fact]
        public void TheBaseFamilyIsTheIdentityOfProductAndTheEmptyFamilyIsItsZero()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd empty = manager.Empty;
            Zdd @base = manager.Base;

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 40, seed: 444))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                Assert.Equal(f, f * @base);
                Assert.Equal(f, @base * f);
                Assert.Equal(empty, f * empty);
                Assert.Equal(empty, empty * f);
            }
        }

        // ---- 境界的な入力（XML doc の記述と突き合わせる）----

        [Fact]
        public void DividingByTheBaseFamilyChangesNothingAndLeavesNoRemainder()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd @base = manager.Base;

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 555))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                // a ∪ ∅ = a なので、条件は「a ∈ f」だけ。割り切れる。
                Assert.Equal(f, f / @base);
                Assert.True((f % @base).IsEmpty);
            }
        }

        [Fact]
        public void DividingByTheEmptyFamilyIsTheWholePowerSet()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd empty = manager.Empty;
            BruteForceFamily powerSet = BruteForceFamily.PowerSet(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 666))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                // 「∀ b ∈ ∅」は空虚に真なので、定義どおりならすべての部分集合が商に入る。
                FamilyAssert.AssertSameFamily("f / ∅", f / empty, powerSet, family);

                // 商が何であれ ∅ を掛ければ ∅ なので、余りは f のまま。定義式もそのまま成り立つ。
                Assert.Equal(f, f % empty);
                Assert.Equal(f, (f / empty * empty) | (f % empty));
            }
        }

        [Fact]
        public void DividingByTheEmptyFamilyUsesTheManagersVariables()
        {
            // 冪集合の形は変数の個数だけで決まる。0 変数なら 2^∅ = {∅}。
            using ZddManager none = new ZddManager(0);
            Assert.Equal(none.Base, none.Base / none.Empty);

            using ZddManager three = new ZddManager(3);
            FamilyAssert.AssertSameFamily(
                "{∅} / ∅",
                three.Base / three.Empty,
                BruteForceFamily.PowerSet(3));

            // ノードは変数の個数ぶんしか作らない（族としては 8 個の集合を持つ）。
            Assert.Equal(3L, (three.Base / three.Empty).NodeCount);
        }

        [Fact]
        public void TheEmptyDividendStaysEmpty()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd empty = manager.Empty;

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 777))
            {
                Zdd g = ZddFamilies.Build(manager, family);

                if (g.IsEmpty)
                {
                    // ∅ / ∅ は冪集合（上の規約どおり）。ここで見たいのはそれ以外。
                    continue;
                }

                Assert.True((empty / g).IsEmpty);
                Assert.True((empty % g).IsEmpty);
            }
        }

        [Fact]
        public void AFamilyDividedByItselfIsTheBaseFamily()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 888))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                if (f.IsEmpty)
                {
                    continue;
                }

                Assert.Equal(manager.Base, f / f);
                Assert.True((f % f).IsEmpty);
            }
        }

        [Fact]
        public void TheTerminalsCombineAsExpected()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd empty = manager.Empty;
            Zdd @base = manager.Base;

            Assert.Equal(empty, empty * empty);
            Assert.Equal(empty, empty * @base);
            Assert.Equal(@base, @base * @base);

            Assert.Equal(@base, @base / @base);
            Assert.Equal(empty, empty / @base);
            Assert.Equal(empty, @base % @base);

            // ∅ / ∅ も「∀ b ∈ ∅」が空虚に真なので冪集合になる。
            FamilyAssert.AssertSameFamily("∅ / ∅", empty / empty, BruteForceFamily.PowerSet(4));
            Assert.Equal(empty, empty % empty);
        }

        [Fact]
        public void TheHandComputedAnswersMatch()
        {
            using ZddManager manager = new ZddManager(3);

            // h = {{0, 1}, {0, 2}, {1}, {2}} を {{0}} で割ると {{1}, {2}}、余りも {{1}, {2}}。
            Zdd h = ZddFamilies.Build(manager, [0, 1], [0, 2], [1], [2]);
            Zdd divisor = ZddFamilies.Build(manager, [0]);
            BruteForceFamily expected = BruteForceFamily.FromSets(3, [1], [2]);

            FamilyAssert.AssertSameFamily("h / {{0}}", h / divisor, expected);
            FamilyAssert.AssertSameFamily("h % {{0}}", h % divisor, expected);

            // 積は「1 つずつ採って和を作る」。{{0}, {1}} * {{1}, {2}} = {{0,1}, {0,2}, {1}, {1,2}}。
            Zdd left = ZddFamilies.Build(manager, [0], [1]);
            Zdd right = ZddFamilies.Build(manager, [1], [2]);

            FamilyAssert.AssertSameFamily(
                "{{0}, {1}} * {{1}, {2}}",
                left * right,
                BruteForceFamily.FromSets(3, [0, 1], [0, 2], [1], [1, 2]));
        }

        [Fact]
        public void TheOperatorsAreTheSameOperations()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 999))
            {
                Assert.Equal(f.Product(g), f * g);
                Assert.Equal(f.Quotient(g), f / g);
                Assert.Equal(f.Remainder(g), f % g);
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
                FamilyCases.RandomFamilies(VariableCount, 8, seed: 31337).ToArray();

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

            // 偶数個の集合と奇数個の集合を足すと、空集合以外のあらゆる部分集合が作れる
            // （|s| が奇数なら ∅ ∪ s、偶数かつ 1 個以上なら s ∪ {s の元 1 個}）。
            Assert.Equal(powerSet - manager.Base, even * odd);

            // 冪集合は掛けても割っても動かない。
            Assert.Equal(powerSet, powerSet * powerSet);
            Assert.Equal(powerSet, powerSet / manager.Base);
            Assert.True((powerSet % powerSet).IsEmpty);
        }

        // ---- 深い ZDD（スタックオーバーフロー回帰テスト） ----

        [Fact]
        public void DeepDiagramsDoNotOverflowTheStack()
        {
            // 変数 10 万。素直な再帰実装ならここで StackOverflowException になり、
            // .NET では catch できずプロセスごと落ちる（docs/PLAN.md §4.5）。
            const int VariableCount = 100_000;

            using ZddManager manager = new ZddManager(VariableCount);

            // 集合を 1 つだけ持つ族。all = {全 item}、evens = {偶数 item}、odds = {奇数 item}。
            Zdd all = BuildSingleSet(manager, item => true);
            Zdd evens = BuildSingleSet(manager, item => item % 2 == 0);
            Zdd odds = BuildSingleSet(manager, item => item % 2 != 0);

            Assert.Equal((long)VariableCount, all.NodeCount);

            // 集合が 1 つずつなら、積は「その 2 つの和」1 個だけの族になる。
            Assert.Equal(all, evens * odds);
            Assert.Equal(all, all * all);
            Assert.Equal(all, all * evens);

            // {全 item} を {偶数 item} で割ると {奇数 item}。割り切れる。
            Assert.Equal(odds, all / evens);
            Assert.True((all % evens).IsEmpty);

            // 偶数 item しか持たない族は、奇数 item を含む集合を作れないので割り切れない。
            Assert.True((evens / odds).IsEmpty);
            Assert.Equal(evens, evens % odds);
        }

        // ---- アロケーション ----

#if DEBUG
        [Fact(Skip = "Debug ビルドでは Debug.Assert のメッセージ生成そのものがアロケートするため、Release でのみ測る。")]
#else
        [Fact]
#endif
        public void TheHotPathDoesNotAllocate()
        {
            const int VariableCount = 8;

            // キャッシュを切って、毎回いちばん長い経路（全部分問題の走査）を通す。
            ZddManagerOptions disabled = new ZddManagerOptions { InitialCacheCapacity = 0, MaxCacheCapacity = 0 };
            using ZddManager manager = new ZddManager(VariableCount, disabled);

            Zdd f = ZddFamilies.Build(manager, BruteForceFamily.Random(VariableCount, 0.2, seed: 4242));
            Zdd g = ZddFamilies.Build(manager, BruteForceFamily.Random(VariableCount, 0.2, seed: 2424));

            // 先に JIT を通し、作業領域とノードを出揃わせる。測るのは定常状態のアロケーション。
            Exercise(f, g, 20);

            long before = GC.GetAllocatedBytesForCurrentThread();
            Exercise(f, g, 200);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0L, after - before);
        }

        // ---- 引数の検査 ----

        [Fact]
        public void AFamilyFromAnotherManagerIsRejected()
        {
            using ZddManager one = new ZddManager(4);
            using ZddManager other = new ZddManager(4);

            Zdd native = one.Singleton(0);
            Zdd foreign = other.Singleton(0);

            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.Product(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.Quotient(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.Remainder(foreign)).ParamName);

            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Product(foreign, native)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Quotient(foreign, native)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Remainder(foreign, native)).ParamName);
        }

        [Fact]
        public void ADefaultHandleHasNoOperations()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd none = default;
            Zdd zdd = manager.Singleton(0);

            // 左辺が default なら、そもそも所有マネージャが分からない。
            Assert.Throws<InvalidOperationException>(() => none * zdd);
            Assert.Throws<InvalidOperationException>(() => none / zdd);
            Assert.Throws<InvalidOperationException>(() => none % zdd);

            // 右辺が default なら、どのマネージャにも属さない族を混ぜた誤用として弾く。
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd * none).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd / none).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd % none).ParamName);
        }

        [Fact]
        public void OperationsOnADisposedManagerThrow()
        {
            ZddManager manager = new ZddManager(4);
            Zdd f = manager.Singleton(1);
            Zdd g = manager.Singleton(2);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => f * g);
            Assert.Throws<ObjectDisposedException>(() => f / g);
            Assert.Throws<ObjectDisposedException>(() => f % g);
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
            FamilyAssert.AssertSameFamily("f * g", left.Product(right), f.Product(g), g);
            FamilyAssert.AssertSameFamily("f / g", left.Quotient(right), f.Quotient(g), g);
            FamilyAssert.AssertSameFamily("f % g", left.Remainder(right), f.Remainder(g), g);
        }

        private static void Exercise(in Zdd f, in Zdd g, int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                _ = f * g;
                _ = f / g;
                _ = f % g;
            }
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
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd[] families = FamilyCases.RandomFamilies(VariableCount, 6, seed)
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
        /// 集合を 1 つだけ持つ族 <c>{ { i : include(i) } }</c> を、葉から根へ 1 段ずつ積んで作る。
        /// </summary>
        private static Zdd BuildSingleSet(ZddManager manager, Func<int, bool> include)
        {
            Zdd result = manager.Base;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                if (include(item))
                {
                    result = manager.CreateNode(item, manager.Empty, result);
                }
            }

            return result;
        }
    }
}

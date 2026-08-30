using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="Zdd.Maximal"/> / <see cref="Zdd.Minimal"/> / <see cref="Zdd.HittingSets"/> /
    /// <see cref="Zdd.Complement"/> / <see cref="Zdd.Flip"/> の検証。
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="BruteForceFamily"/>（定義をそのままループで書いた素朴実装）で、
    /// 比較は <see cref="FamilyAssert.AssertSameFamily(string?, in Zdd, BruteForceFamily, BruteForceFamily?)"/>
    /// が行う。総当たりの回し方は <see cref="FamilyCases"/> にある。
    /// </remarks>
    public class ExtremalOperationTests
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
                AssertOperationsMatchNaive(manager, family);
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void EveryFamilyOfFourVariablesMatchesTheNaiveImplementation()
        {
            const int VariableCount = FamilyCases.AllFamiliesVariableLimit;

            using ZddManager manager = new ZddManager(VariableCount);

            // 4 変数の族は 2^16 = 65536 通り。極大・極小もヒッティング集合も、
            // 「相手がいない」「相手が全部」の両極端を含めて全部通る。
            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertOperationsMatchNaive(manager, family);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(8)]
        [InlineData(FamilyCases.DefaultVariableCount)]
        [InlineData(FamilyCases.ExhaustiveVariableLimit)]
        public void RandomFamiliesMatchTheNaiveImplementation(int variableCount)
        {
            using ZddManager manager = new ZddManager(variableCount);

            foreach (BruteForceFamily family in
                FamilyCases.RandomFamilies(variableCount, 10, seed: 20260930 + variableCount))
            {
                AssertOperationsMatchNaive(manager, family);
            }
        }

        [Fact]
        public void EverySingleSetFamilyMatchesTheNaiveImplementation()
        {
            const int VariableCount = 7;

            using ZddManager manager = new ZddManager(VariableCount);

            // 集合を 1 つだけ持つ族。極大＝極小＝自分自身で、ヒッティング集合は
            // 「その集合と交わる部分集合すべて」になる。2^7 = 128 個すべてを回す。
            foreach (int mask in FamilyCases.AllSubsets(VariableCount))
            {
                AssertOperationsMatchNaive(manager, BruteForceFamily.FromMasks(VariableCount, [mask]));
            }

            // 冪集合と、その裏返しである ∅ / {∅} も 1 度ずつ。
            AssertOperationsMatchNaive(manager, BruteForceFamily.PowerSet(VariableCount));
            AssertOperationsMatchNaive(manager, BruteForceFamily.Empty(VariableCount));
            AssertOperationsMatchNaive(manager, BruteForceFamily.Base(VariableCount));
        }

        // ---- 極大・極小の性質 ----

        [Fact]
        public void TheExtremalFamiliesAreAntichains()
        {
            foreach (Zdd f in Families(seed: 3001))
            {
                Zdd minimal = f.Minimal();
                Zdd maximal = f.Maximal();

                // 反鎖なら、極大を取っても極小を取っても自分自身のまま
                // （どの 2 つも包含関係にないので、落ちる要素が無い）。
                Assert.Equal(minimal, minimal.Minimal());
                Assert.Equal(minimal, minimal.Maximal());
                Assert.Equal(maximal, maximal.Maximal());
                Assert.Equal(maximal, maximal.Minimal());
            }
        }

        [Fact]
        public void TheExtremalFamiliesAreSubfamiliesThatKeepTheirOwnExtremes()
        {
            foreach (Zdd f in Families(seed: 3002))
            {
                Zdd minimal = f.Minimal();
                Zdd maximal = f.Maximal();

                // 集合そのものは作り替えないので、結果は必ず f の部分族。
                Assert.Equal(minimal, minimal & f);
                Assert.Equal(maximal, maximal & f);

                // 極小元は「f のどれかを真に含む」ことがなく、極大元は「f のどれかに真に含まれる」
                // ことがない。ふるいで書き直すと、極小は「自分より小さい相手がいない」＝
                // 自分以外の上位集合を持たない要素の集まりになる。
                Assert.Equal(minimal, f.NonSupersetsOf(f - minimal));
                Assert.Equal(maximal, f.NonSubsetsOf(f - maximal));
            }
        }

        [Fact]
        public void TheExtremalFamiliesAreEmptyOnlyWhenTheFamilyIs()
        {
            foreach (Zdd f in Families(seed: 3003))
            {
                // 有限の族には必ず極大元と極小元がある。
                Assert.Equal(f.IsEmpty, f.Minimal().IsEmpty);
                Assert.Equal(f.IsEmpty, f.Maximal().IsEmpty);
            }
        }

        [Fact]
        public void AFamilyWithTheEmptySetHasItAsItsOnlyMinimalElement()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 3004))
            {
                // ∅ を足せば、それが唯一の極小元になる（∅ はどの集合にも真に含まれる）。
                Zdd withEmptySet = ZddFamilies.Build(manager, family) | manager.Base;

                Assert.Equal(manager.Base, withEmptySet.Minimal());
            }
        }

        // ---- ヒッティング集合の性質 ----

        [Fact]
        public void TheHittingSetsAreClosedUpwards()
        {
            foreach (Zdd f in Families(seed: 4001))
            {
                Zdd hitting = f.HittingSets();

                // a が全員と交わるなら、a を含む集合も全員と交わる。
                // 冪集合を掛ける（＝あらゆる集合を足し合わせる）と、上に閉じた族は自分自身に戻る。
                Assert.Equal(hitting, hitting * PowerSetOf(f.Manager));
            }
        }

        [Fact]
        public void TheHittingSetsOfAnAntichainAreDualToIt()
        {
            foreach (Zdd f in Families(seed: 4002))
            {
                // Berge の双対定理: 反鎖の極小ヒッティング集合を 2 回取ると元の反鎖に戻る。
                // この API が返すのは極小なものだけではなく「交わる集合すべて」なので、
                // 双対を見るには極小化を挟む（docs/PLAN.md §5.2）。
                Zdd antichain = f.Minimal();
                Zdd blocker = antichain.HittingSets().Minimal();

                Assert.Equal(antichain, blocker.HittingSets().Minimal());
            }
        }

        [Fact]
        public void TheHittingSetsTurnUnionsIntoIntersections()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 4003))
            {
                // 「f ∪ g の全員と交わる」ことは「f の全員と交わり、かつ g の全員と交わる」ことと同じ。
                Assert.Equal((f | g).HittingSets(), f.HittingSets() & g.HittingSets());
            }
        }

        [Fact]
        public void TheHittingSetsOfTheBoundaryFamiliesAreTheExpectedOnes()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            // 条件が 1 つも無いので、どの部分集合も答になる。
            Assert.Equal(PowerSetOf(manager), manager.Empty.HittingSets());

            // ∅ と交われる集合は無い。空集合を持つ族はすべてこうなる。
            Assert.True(manager.Base.HittingSets().IsEmpty);
            Assert.True((manager.Singleton(0) | manager.Base).HittingSets().IsEmpty);

            // 1 要素集合を叩けるのはその item を含む集合だけ。
            Zdd containsItemZero = manager.Singleton(0) * PowerSetOf(manager);
            Assert.Equal(containsItemZero, manager.Singleton(0).HittingSets());

            // 別名は同じ演算。
            foreach (Zdd f in Families(seed: 4004))
            {
                Assert.Equal(f.HittingSets(), f.Blocking());
            }
        }

        [Fact]
        public void UnusedItemsStillCountTowardsTheHittingSets()
        {
            // 全体集合はマネージャの全変数で、Support ではない（docs/OPEN-QUESTIONS.md B8）。
            // 同じ内容の族でも、変数が 1 つ増えればヒッティング集合は 2 倍になる。
            using ZddManager narrow = new ZddManager(1);
            using ZddManager wide = new ZddManager(2);

            Assert.Equal(1, ZddFamilies.ToBruteForce(narrow.Singleton(0).HittingSets()).Count);
            Assert.Equal(2, ZddFamilies.ToBruteForce(wide.Singleton(0).HittingSets()).Count);
        }

        // ---- 補の性質 ----

        [Fact]
        public void TheComplementIsAnInvolutionThatSplitsThePowerSet()
        {
            foreach (Zdd f in Families(seed: 5001))
            {
                Zdd powerSet = PowerSetOf(f.Manager);

                Assert.Equal(f, ~~f);
                Assert.Equal(f.Complement(), ~f);

                // 補は冪集合をちょうど 2 つに分ける（重ならず、合わせて 2^U）。
                Assert.Equal(powerSet, f | ~f);
                Assert.True((f & ~f).IsEmpty);
                Assert.Equal(~f, powerSet - f);
            }
        }

        [Fact]
        public void TheComplementOfTheBoundaryFamiliesIsTheExpectedOne()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd powerSet = PowerSetOf(manager);

            Assert.Equal(powerSet, ~manager.Empty);
            Assert.Equal(manager.Empty, ~powerSet);

            // 商の f / ∅ も同じ全体集合を指す（冪集合の組み立てはマネージャの 1 箇所にまとめてある）。
            Assert.Equal(powerSet, manager.Singleton(0) / manager.Empty);

            // 全体集合はマネージャの変数の個数で決まるので、補の大きさもそれで決まる。
            Assert.Equal((1 << VariableCount) - 1, ZddFamilies.ToBruteForce(~manager.Base).Count);

            using ZddManager empty = new ZddManager(0);

            // 変数が 1 つも無ければ 2^U = {∅} なので、∅ の補は {∅}、{∅} の補は ∅。
            Assert.Equal(empty.Base, ~empty.Empty);
            Assert.Equal(empty.Empty, ~empty.Base);
        }

        [Fact]
        public void TheComplementUsesEveryVariableOfTheManagerAndNotJustTheSupport()
        {
            // 同じ内容の族でも、変数が 1 つ増えれば補は倍の大きさになる（docs/OPEN-QUESTIONS.md B8）。
            using ZddManager narrow = new ZddManager(1);
            using ZddManager wide = new ZddManager(2);

            Assert.Equal(1, ZddFamilies.ToBruteForce(~narrow.Singleton(0)).Count);
            Assert.Equal(3, ZddFamilies.ToBruteForce(~wide.Singleton(0)).Count);
        }

        // ---- Flip ----

        [Fact]
        public void FlipIsAChangeAppliedToEveryListedItem()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 6001))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                // 何も渡さなければ族はそのまま。
                Assert.Equal(zdd, zdd.Flip());

                for (int item = 0; item < VariableCount; item++)
                {
                    Assert.Equal(zdd.Change(item), zdd.Flip(item));
                }

                Assert.Equal(zdd.Change(0).Change(3).Change(5), zdd.Flip(0, 3, 5));

                // item どうしの順序は結果に影響しない。
                Assert.Equal(zdd.Flip(0, 3, 5), zdd.Flip(5, 0, 3));

                // 同じ item を 2 度渡すと反転が打ち消し合う。
                Assert.Equal(zdd.Flip(2), zdd.Flip(2, 4, 4));

                // 2 回かければ必ず元に戻る。
                Assert.Equal(zdd, zdd.Flip(1, 2, 3).Flip(1, 2, 3));
            }
        }

        [Fact]
        public void FlipMatchesTheNaiveImplementation()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            int[] items = [0, 2, 3];

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 6002))
            {
                BruteForceFamily expected = family;

                foreach (int item in items)
                {
                    expected = expected.Change(item);
                }

                FamilyAssert.AssertSameFamily(
                    "Flip(0, 2, 3)",
                    ZddFamilies.Build(manager, family).Flip(items),
                    expected,
                    family);
            }
        }

        // ---- キャッシュ ----

        [Fact]
        public void ResultsAreTheSameWithAndWithoutTheOperationCache()
        {
            const int VariableCount = 8;

            ZddManagerOptions disabled = new ZddManagerOptions { InitialCacheCapacity = 0, MaxCacheCapacity = 0 };

            using ZddManager cached = new ZddManager(VariableCount);
            using ZddManager uncached = new ZddManager(VariableCount, disabled);

            Assert.True(cached.Cache.IsEnabled);
            Assert.False(uncached.Cache.IsEnabled);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 8, seed: 727272))
            {
                AssertOperationsMatchNaive(cached, family);
                AssertOperationsMatchNaive(uncached, family);
            }
        }

        [Fact]
        public void SharedSubproblemsAreVisitedOnceEvenWithoutTheOperationCache()
        {
            // 「要素数が偶数の部分集合」。段ごとに 2 状態しか無いのでノードは高々 2n 個だが、
            // パスは 2^(n-1) 本ある。途中結果表が効いていなければ、以下の演算は終わらない。
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

            Zdd powerSet = PowerSetOf(manager);
            Zdd universe = BuildSingleSet(manager, item => true);

            // 偶数個の集合と奇数個の集合で冪集合をちょうど 2 つに分けている。
            Assert.Equal(odd, ~even);
            Assert.Equal(powerSet, even | odd);

            // 偶数側の極小元は ∅ だけ。極大元は U だけで、変数が 64 個＝偶数なので U は偶数側にいる
            // （U でない偶数集合は、外の 2 つを足せばもっと大きな偶数集合になる）。
            Assert.Equal(manager.Base, even.Minimal());
            Assert.Equal(universe, even.Maximal());

            // 奇数側の極小元は 1 要素集合すべて、極大元は「item を 1 つだけ欠いた集合」すべて。
            Zdd singletons = manager.Empty;
            Zdd coSingletons = manager.Empty;

            for (int item = 0; item < VariableCount; item++)
            {
                singletons |= manager.Singleton(item);
                coSingletons |= universe.Change(item);
            }

            Assert.Equal(singletons, odd.Minimal());
            Assert.Equal(coSingletons, odd.Maximal());

            // 1 要素集合すべてを叩けるのは、全 item を持つ集合＝ U だけ。
            Assert.Equal(universe, odd.Minimal().HittingSets());
        }

        // ---- 深い ZDD（スタックオーバーフロー回帰テスト） ----

        [Fact]
        public void DeepDiagramsDoNotOverflowTheStack()
        {
            // 変数 10 万。素直な再帰実装ならここで StackOverflowException になり、
            // .NET では catch できずプロセスごと落ちる（docs/PLAN.md §4.5）。
            const int VariableCount = 100_000;

            using ZddManager manager = new ZddManager(VariableCount);

            // 集合を 1 つだけ持つ族。all = {全 item}、evens = {偶数 item}。
            Zdd all = BuildSingleSet(manager, item => true);
            Zdd evens = BuildSingleSet(manager, item => item % 2 == 0);

            Assert.Equal((long)VariableCount, all.NodeCount);

            // 集合が 1 つだけなら、それが唯一の極大元であり極小元でもある。
            Assert.Equal(all, all.Minimal());
            Assert.Equal(all, all.Maximal());

            // evens ⊂ all なので、極小は evens、極大は all。
            Assert.Equal(evens, (all | evens).Minimal());
            Assert.Equal(all, (all | evens).Maximal());

            // 全 item から成る集合を叩けるのは、∅ 以外のすべての部分集合。
            Assert.Equal(PowerSetOf(manager) - manager.Base, all.HittingSets());

            // 補の二重適用は元に戻る（途中の族は 2^100000 - 1 個の集合を持つ）。
            Assert.Equal(all, ~~all);
            Assert.Equal(PowerSetOf(manager), all | ~all);

            // 一括反転も深いまま動く（item 0 と 1 を落とすと、残り全部の集合になる）。
            Assert.Equal(all.Change(0).Change(1), all.Flip(0, 1));
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

            Zdd f = ZddFamilies.Build(manager, BruteForceFamily.Random(VariableCount, 0.2, seed: 8484));

            // 先に JIT を通し、作業領域とノードを出揃わせる。測るのは定常状態のアロケーション。
            Exercise(f, 20);

            long before = GC.GetAllocatedBytesForCurrentThread();
            Exercise(f, 200);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0L, after - before);
        }

        // ---- 引数の検査 ----

        [Fact]
        public void AFamilyFromAnotherManagerIsRejected()
        {
            using ZddManager one = new ZddManager(4);
            using ZddManager other = new ZddManager(4);

            Zdd foreign = other.Singleton(0);

            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Maximal(foreign)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Minimal(foreign)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.HittingSets(foreign)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Complement(foreign)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Flip(foreign, [1])).ParamName);
        }

        [Fact]
        public void ADefaultHandleHasNoOperations()
        {
            Zdd none = default;

            Assert.Throws<InvalidOperationException>(() => none.Maximal());
            Assert.Throws<InvalidOperationException>(() => none.Minimal());
            Assert.Throws<InvalidOperationException>(() => none.HittingSets());
            Assert.Throws<InvalidOperationException>(() => none.Blocking());
            Assert.Throws<InvalidOperationException>(() => none.Complement());
            Assert.Throws<InvalidOperationException>(() => ~none);
            Assert.Throws<InvalidOperationException>(() => none.Flip(0));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        [InlineData(int.MaxValue)]
        public void AnItemOutsideTheManagerIsRejectedByFlip(int item)
        {
            using ZddManager manager = new ZddManager(4);

            Zdd zdd = manager.Singleton(0);

            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.Flip(item)).ParamName);

            // 範囲外が混ざっていたら、手前の item も反転しない（検査は計算の前に済ませる）。
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.Flip(1, item)).ParamName);
        }

        [Fact]
        public void OperationsOnADisposedManagerThrow()
        {
            ZddManager manager = new ZddManager(4);
            Zdd zdd = manager.Singleton(1);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => zdd.Maximal());
            Assert.Throws<ObjectDisposedException>(() => zdd.Minimal());
            Assert.Throws<ObjectDisposedException>(() => zdd.HittingSets());
            Assert.Throws<ObjectDisposedException>(() => zdd.Complement());
            Assert.Throws<ObjectDisposedException>(() => zdd.Flip(1));
            Assert.Throws<ObjectDisposedException>(() => zdd.Flip());
        }

        // ---- 照合の本体 ----

        private static void AssertOperationsMatchNaive(ZddManager manager, BruteForceFamily family)
        {
            Zdd zdd = ZddFamilies.Build(manager, family);

            // 組み立て自体が壊れていたら、以降の照合は何も言っていないことになる。
            FamilyAssert.AssertSameFamily("the family builder", zdd, family);

            FamilyAssert.AssertSameFamily("f.Maximal()", zdd.Maximal(), family.Maximal(), family);
            FamilyAssert.AssertSameFamily("f.Minimal()", zdd.Minimal(), family.Minimal(), family);
            FamilyAssert.AssertSameFamily("f.HittingSets()", zdd.HittingSets(), family.HittingSets(), family);
            FamilyAssert.AssertSameFamily("f.Complement()", zdd.Complement(), family.Complement(), family);
        }

        private static void Exercise(in Zdd f, int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                _ = f.Maximal();
                _ = f.Minimal();
                _ = f.HittingSets();
                _ = f.Complement();
                _ = f.Flip(1);
            }
        }

        // ---- 検証で使う族の作り置き ----

        /// <summary>全体集合の冪集合 2^U。演算 API を使わずに葉から積み上げる。</summary>
        private static Zdd PowerSetOf(ZddManager manager)
        {
            Zdd result = manager.Base;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                result = manager.CreateNode(item, result, result);
            }

            return result;
        }

        /// <summary>性質の検証に使う族を返す。マネージャは呼び出しごとに使い捨てる。</summary>
        private static IEnumerable<Zdd> Families(int seed)
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 24, seed))
            {
                yield return ZddFamilies.Build(manager, family);
            }
        }

        /// <summary>性質の検証に使う族の対を返す。</summary>
        private static IEnumerable<(Zdd F, Zdd G)> Pairs(int seed)
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd[] families = FamilyCases.RandomFamilies(VariableCount, 10, seed)
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

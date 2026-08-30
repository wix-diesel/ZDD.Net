using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="Zdd.Meet"/> / <see cref="Zdd.SupersetsOf"/>（Restrict）/
    /// <see cref="Zdd.SubsetsOf"/>（Permit）/ <see cref="Zdd.NonSubsetsOf"/> /
    /// <see cref="Zdd.NonSupersetsOf"/> の検証。
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="BruteForceFamily"/>（定義をそのままループで書いた素朴実装）で、
    /// 比較は <see cref="FamilyAssert.AssertSameFamily(string?, in Zdd, BruteForceFamily, BruteForceFamily?)"/>
    /// が行う。総当たりの回し方は <see cref="FamilyCases"/> にある。
    /// </remarks>
    public class ContainmentOperationTests
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
                FamilyCases.RandomFamilies(variableCount, 10, seed: 20260901 + variableCount).ToArray();

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
            // 「相手を含む／含まれる」の両方が全パターン通り、ふるいが空になる場合も丸ごと残る場合も出る。
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

        // ---- 否定版と本体の関係 ----

        [Fact]
        public void TheNegatedFiltersAreExactlyWhatTheirCounterpartsLeaveBehind()
        {
            // 実装は差を取らずに 1 回の走査で求めているので、この 2 式は定義の言い換えではなく
            // 「別々に書いた 2 つの走査が噛み合っているか」の検査になる。
            foreach ((Zdd f, Zdd g) in Pairs(seed: 1001))
            {
                Assert.Equal(f - f.SupersetsOf(g), f.NonSupersetsOf(g));
                Assert.Equal(f - f.SubsetsOf(g), f.NonSubsetsOf(g));

                // 残す側と捨てる側で f をちょうど 2 つに分ける（重ならず、合わせて f）。
                Assert.Equal(f, f.SupersetsOf(g) | f.NonSupersetsOf(g));
                Assert.Equal(f, f.SubsetsOf(g) | f.NonSubsetsOf(g));
                Assert.True((f.SupersetsOf(g) & f.NonSupersetsOf(g)).IsEmpty);
                Assert.True((f.SubsetsOf(g) & f.NonSubsetsOf(g)).IsEmpty);
            }
        }

        [Fact]
        public void EveryFilterReturnsASubfamilyOfTheLeftOperand()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 1002))
            {
                foreach (Zdd filtered in new[]
                {
                    f.SupersetsOf(g),
                    f.SubsetsOf(g),
                    f.NonSubsetsOf(g),
                    f.NonSupersetsOf(g),
                })
                {
                    // 集合そのものは作り替えないので、結果は必ず f の部分族。
                    Assert.Equal(filtered, filtered & f);
                }
            }
        }

        // ---- 別名 API ----

        [Fact]
        public void TheAliasesAreTheSameOperations()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 1003))
            {
                // SAPPOROBDD 由来の名前と .NET 的な名前は、同じ実装を指す薄いラッパ。
                Assert.Equal(f.SupersetsOf(g), f.Restrict(g));
                Assert.Equal(f.SubsetsOf(g), f.Permit(g));
            }
        }

        // ---- Meet の代数法則 ----

        [Fact]
        public void MeetIsCommutative()
        {
            foreach ((Zdd f, Zdd g) in Pairs(seed: 1111))
            {
                Assert.Equal(f.Meet(g), g.Meet(f));
            }
        }

        [Fact]
        public void MeetIsAssociative()
        {
            foreach ((Zdd f, Zdd g, Zdd h) in Triples(seed: 2222))
            {
                Assert.Equal(f.Meet(g).Meet(h), f.Meet(g.Meet(h)));
            }
        }

        [Fact]
        public void MeetDistributesOverUnion()
        {
            foreach ((Zdd f, Zdd g, Zdd h) in Triples(seed: 3333))
            {
                Assert.Equal(f.Meet(g | h), f.Meet(g) | f.Meet(h));
                Assert.Equal((g | h).Meet(f), g.Meet(f) | h.Meet(f));
            }
        }

        [Fact]
        public void TheBaseFamilyAbsorbsMeetAndTheEmptyFamilyIsItsZero()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd empty = manager.Empty;
            Zdd @base = manager.Base;

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 40, seed: 4444))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                // ∅ との交わりは常に ∅ なので、できる族は {∅} 1 通りだけ（f が空でなければ）。
                Assert.Equal(f.IsEmpty ? empty : @base, f.Meet(@base));
                Assert.Equal(f.IsEmpty ? empty : @base, @base.Meet(f));

                Assert.Equal(empty, f.Meet(empty));
                Assert.Equal(empty, empty.Meet(f));
            }
        }

        // ---- 境界的な入力（XML doc の記述と突き合わせる）----

        [Fact]
        public void RestrictingByTheBaseFamilyKeepsEverything()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd @base = manager.Base;

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 5555))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                // ∅ はどの集合にも含まれるので、Restrict は全員を残し、その否定版は誰も残さない。
                Assert.Equal(f, f.Restrict(@base));
                Assert.True(f.NonSupersetsOf(@base).IsEmpty);
            }
        }

        [Fact]
        public void PermittingByTheBaseFamilyKeepsOnlyTheEmptySet()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd @base = manager.Base;

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 6666))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                // a ⊆ ∅ を満たすのは a = ∅ だけ。f が空集合を持つかどうかで答が変わる。
                bool hasEmptySet = family.Contains(0);

                Assert.Equal(hasEmptySet ? @base : manager.Empty, f.Permit(@base));
                Assert.Equal(hasEmptySet ? f - @base : f, f.NonSubsetsOf(@base));
            }
        }

        [Fact]
        public void TheEmptySetInTheRightOperandIsNotIgnored()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd f = ZddFamilies.Build(manager, [], [0], [0, 1], [2]);
            Zdd withEmptySet = ZddFamilies.Build(manager, [], [0, 1]);

            // ∅ はどの集合にも含まれるので、Restrict は f を丸ごと残す。
            FamilyAssert.AssertSameFamily(
                "f.Restrict({∅, {0,1}})",
                f.Restrict(withEmptySet),
                BruteForceFamily.FromSets(3, [], [0], [0, 1], [2]));

            // Permit 側は「∅ に含まれる」＝ ∅ 自身と、{0, 1} に含まれるものが残る。
            FamilyAssert.AssertSameFamily(
                "f.Permit({∅, {0,1}})",
                f.Permit(withEmptySet),
                BruteForceFamily.FromSets(3, [], [0], [0, 1]));

            FamilyAssert.AssertSameFamily(
                "f.NonSubsetsOf({∅, {0,1}})",
                f.NonSubsetsOf(withEmptySet),
                BruteForceFamily.FromSets(3, [2]));

            Assert.True(f.NonSupersetsOf(withEmptySet).IsEmpty);
        }

        [Fact]
        public void TheEmptyRightOperandLeavesTheFamilyUntouchedOrEmpty()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd empty = manager.Empty;

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 7777))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                // 「∃ b ∈ ∅」は偽、「∀ b ∈ ∅」は空虚に真。
                Assert.True(f.SupersetsOf(empty).IsEmpty);
                Assert.True(f.SubsetsOf(empty).IsEmpty);
                Assert.Equal(f, f.NonSubsetsOf(empty));
                Assert.Equal(f, f.NonSupersetsOf(empty));

                // 左が空なら、ふるいにかける候補が無い。
                Assert.True(empty.SupersetsOf(f).IsEmpty);
                Assert.True(empty.SubsetsOf(f).IsEmpty);
                Assert.True(empty.NonSubsetsOf(f).IsEmpty);
                Assert.True(empty.NonSupersetsOf(f).IsEmpty);
            }
        }

        [Fact]
        public void AFamilyFilteredByItselfIsAllOrNothing()
        {
            const int VariableCount = 5;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 8888))
            {
                Zdd f = ZddFamilies.Build(manager, family);

                // a ⊆ a も a ⊇ a も成り立つので、どの候補も自分自身を相手に見つける。
                Assert.Equal(f, f.SupersetsOf(f));
                Assert.Equal(f, f.SubsetsOf(f));
                Assert.True(f.NonSubsetsOf(f).IsEmpty);
                Assert.True(f.NonSupersetsOf(f).IsEmpty);
            }
        }

        [Fact]
        public void TheTerminalsCombineAsExpected()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd empty = manager.Empty;
            Zdd @base = manager.Base;

            Assert.Equal(empty, empty.Meet(empty));
            Assert.Equal(empty, empty.Meet(@base));
            Assert.Equal(@base, @base.Meet(@base));

            Assert.Equal(@base, @base.Restrict(@base));
            Assert.Equal(@base, @base.Permit(@base));
            Assert.Equal(empty, @base.NonSubsetsOf(@base));
            Assert.Equal(empty, @base.NonSupersetsOf(@base));

            Assert.Equal(empty, empty.Restrict(@base));
            Assert.Equal(empty, @base.Restrict(empty));
            Assert.Equal(@base, @base.NonSupersetsOf(empty));
        }

        [Fact]
        public void TheHandComputedAnswersMatch()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd f = ZddFamilies.Build(manager, [0], [0, 1], [1, 2], [2]);
            Zdd g = ZddFamilies.Build(manager, [0], [2]);

            // {0} か {2} のどちらかを含むもの。{1, 2} は {2} を含む。
            FamilyAssert.AssertSameFamily(
                "f.Restrict(g)",
                f.Restrict(g),
                BruteForceFamily.FromSets(3, [0], [0, 1], [1, 2], [2]));

            // {0} か {2} に含まれるもの。1 要素の集合だけが残る。
            FamilyAssert.AssertSameFamily(
                "f.Permit(g)",
                f.Permit(g),
                BruteForceFamily.FromSets(3, [0], [2]));

            FamilyAssert.AssertSameFamily(
                "f.NonSubsetsOf(g)",
                f.NonSubsetsOf(g),
                BruteForceFamily.FromSets(3, [0, 1], [1, 2]));

            Assert.True(f.NonSupersetsOf(g).IsEmpty);

            // Meet は「1 つずつ採って交わりを作る」。{{0}, {1,2}} ⊓ {{0,1}, {2}} = {{0}, ∅, {1}, {2}}。
            Zdd left = ZddFamilies.Build(manager, [0], [1, 2]);
            Zdd right = ZddFamilies.Build(manager, [0, 1], [2]);

            FamilyAssert.AssertSameFamily(
                "{{0}, {1,2}} ⊓ {{0,1}, {2}}",
                left.Meet(right),
                BruteForceFamily.FromSets(3, [], [0], [1], [2]));
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
                FamilyCases.RandomFamilies(VariableCount, 8, seed: 424242).ToArray();

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

            Zdd universe = manager.Base;
            for (int item = VariableCount - 1; item >= 0; item--)
            {
                universe = manager.CreateNode(item, manager.Empty, universe);
            }

            // s ≠ U なら s の外に item x を採れて、s と s ∪ {x} は偶奇がちょうど食い違うので
            // どちらかの向きで s が作れる。U だけは a ∩ b = U が a = b = U を強いるので作れない
            // （変数が 64 個＝偶数なので、U は偶数側にしかいない）。
            Assert.Equal(powerSet - universe, even.Meet(odd));

            // 冪集合を相手にすれば、どんな族も丸ごと残る（相手に上位集合も部分集合も必ずいる）。
            Assert.Equal(even, even.Restrict(powerSet));
            Assert.Equal(even, even.Permit(powerSet));
            Assert.True(even.NonSubsetsOf(powerSet).IsEmpty);
            Assert.True(even.NonSupersetsOf(powerSet).IsEmpty);

            // 奇数個の集合は、どれも偶数個の集合の「ちょうど 1 つ違い」の上位集合を持つ。
            Assert.Equal(odd, odd.Restrict(even));
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

            // 集合が 1 つずつなら、Meet は「その 2 つの交わり」1 個だけの族になる。
            Assert.Equal(evens, all.Meet(evens));
            Assert.Equal(manager.Base, evens.Meet(odds));
            Assert.Equal(all, all.Meet(all));

            // {全 item} は {偶数 item} を含み、その逆は成り立たない。
            Assert.Equal(all, all.Restrict(evens));
            Assert.True(evens.Restrict(all).IsEmpty);
            Assert.Equal(evens, evens.Permit(all));
            Assert.True(all.Permit(evens).IsEmpty);

            Assert.True(all.NonSupersetsOf(evens).IsEmpty);
            Assert.Equal(evens, evens.NonSupersetsOf(all));
            Assert.Equal(all, all.NonSubsetsOf(evens));
            Assert.True(evens.NonSubsetsOf(all).IsEmpty);
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

            Zdd f = ZddFamilies.Build(manager, BruteForceFamily.Random(VariableCount, 0.2, seed: 5252));
            Zdd g = ZddFamilies.Build(manager, BruteForceFamily.Random(VariableCount, 0.2, seed: 2525));

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

            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.Meet(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.Restrict(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.Permit(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.SupersetsOf(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.SubsetsOf(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.NonSubsetsOf(foreign)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => native.NonSupersetsOf(foreign)).ParamName);

            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Meet(foreign, native)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.SupersetsOf(foreign, native)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.SubsetsOf(foreign, native)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.NonSubsetsOf(foreign, native)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.NonSupersetsOf(foreign, native)).ParamName);
        }

        [Fact]
        public void ADefaultHandleHasNoOperations()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd none = default;
            Zdd zdd = manager.Singleton(0);

            // 左辺が default なら、そもそも所有マネージャが分からない。
            Assert.Throws<InvalidOperationException>(() => none.Meet(zdd));
            Assert.Throws<InvalidOperationException>(() => none.Restrict(zdd));
            Assert.Throws<InvalidOperationException>(() => none.Permit(zdd));
            Assert.Throws<InvalidOperationException>(() => none.NonSubsetsOf(zdd));
            Assert.Throws<InvalidOperationException>(() => none.NonSupersetsOf(zdd));

            // 右辺が default なら、どのマネージャにも属さない族を混ぜた誤用として弾く。
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd.Meet(none)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd.Restrict(none)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd.Permit(none)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd.NonSubsetsOf(none)).ParamName);
            Assert.Equal("g", Assert.Throws<ArgumentException>(() => zdd.NonSupersetsOf(none)).ParamName);
        }

        [Fact]
        public void OperationsOnADisposedManagerThrow()
        {
            ZddManager manager = new ZddManager(4);
            Zdd f = manager.Singleton(1);
            Zdd g = manager.Singleton(2);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => f.Meet(g));
            Assert.Throws<ObjectDisposedException>(() => f.Restrict(g));
            Assert.Throws<ObjectDisposedException>(() => f.Permit(g));
            Assert.Throws<ObjectDisposedException>(() => f.NonSubsetsOf(g));
            Assert.Throws<ObjectDisposedException>(() => f.NonSupersetsOf(g));
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
            FamilyAssert.AssertSameFamily("f ⊓ g", left.Meet(right), f.Meet(g), g);
            FamilyAssert.AssertSameFamily("f.Restrict(g)", left.Restrict(right), f.Restrict(g), g);
            FamilyAssert.AssertSameFamily("f.Permit(g)", left.Permit(right), f.Permit(g), g);
            FamilyAssert.AssertSameFamily("f.NonSubsetsOf(g)", left.NonSubsetsOf(right), f.NonSubsetsOf(g), g);
            FamilyAssert.AssertSameFamily(
                "f.NonSupersetsOf(g)",
                left.NonSupersetsOf(right),
                f.NonSupersetsOf(g),
                g);
        }

        private static void Exercise(in Zdd f, in Zdd g, int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                _ = f.Meet(g);
                _ = f.SupersetsOf(g);
                _ = f.SubsetsOf(g);
                _ = f.NonSubsetsOf(g);
                _ = f.NonSupersetsOf(g);
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

using System;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="Zdd.MapItems"/> の検証（M6-4, issue #139）: 同じマネージャ内で item を
    /// 張り替える、順序保存の高速経路。
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="BruteForceFamily.MapItems"/>（定義をそのままループで書いた素朴実装）で、
    /// 比較は <see cref="FamilyAssert.AssertSameFamily(string?, in Zdd, BruteForceFamily, BruteForceFamily?)"/>
    /// が行う。この PR は「<c>itemMap</c> が support 上で狭義単調増加」の場合だけを実装するので、
    /// 総当たり照合ではその条件を満たす <c>itemMap</c> だけをランダムに作って試す
    /// （<see cref="BuildMonotonicItemMap"/>）。一般の置換は M6-5 で追加される。
    /// </remarks>
    public class MapItemsTests
    {
        // ---- 総当たり照合 ----

        [Fact]
        public void EveryFamilyOfThreeVariablesMatchesTheNaiveImplementation()
        {
            const int VariableCount = 3;

            using ZddManager manager = new ZddManager(VariableCount);
            Random random = new Random(20260904);

            // 3 変数の集合は 8 個。その部分集合＝族は 2^8 = 256 通りで、すべて試せる。
            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertMapItemsMatchesNaive(manager, family, random, itemMapsPerFamily: 3);
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
            Random random = new Random(20260904 + variableCount);

            foreach (BruteForceFamily family in
                FamilyCases.RandomFamilies(variableCount, 50, seed: 20260904 + variableCount))
            {
                AssertMapItemsMatchesNaive(manager, family, random, itemMapsPerFamily: 3);
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void EverySubsetOfTwelveVariablesMatchesTheNaiveImplementation()
        {
            const int VariableCount = FamilyCases.ExhaustiveVariableLimit;

            using ZddManager manager = new ZddManager(VariableCount);
            Random random = new Random(4649);

            // 1 つの集合だけを持つ族を、2^12 = 4096 個の集合すべてについて回す。
            foreach (int mask in FamilyCases.AllSubsets(VariableCount))
            {
                AssertMapItemsMatchesNaive(
                    manager,
                    BruteForceFamily.FromMasks(VariableCount, [mask]),
                    random,
                    itemMapsPerFamily: 1);
            }

            // 冪集合（support = 全変数）も 1 度。support が全域だと単調な itemMap は恒等写像しかない
            // ので、ここでの目的は「その境界ケースでも壊れない」ことの確認。
            AssertMapItemsMatchesNaive(manager, BruteForceFamily.PowerSet(VariableCount), random, itemMapsPerFamily: 1);
        }

        // ---- 恒等写像 ----

        [Fact]
        public void IdentityMapReturnsTheSameHandleWithoutBuildingAnything()
        {
            const int VariableCount = 8;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd zdd = ZddFamilies.Build(manager, BruteForceFamily.Random(VariableCount, 0.3, seed: 1));
            long nodeCountBefore = manager.NodeCount;

            int[] identity = Enumerable.Range(0, VariableCount).ToArray();
            Zdd mapped = zdd.MapItems(identity);

            Assert.Equal(zdd, mapped);
            Assert.Equal(nodeCountBefore, manager.NodeCount);
        }

        [Fact]
        public void IdentityMapOnTerminalsReturnsTheSameHandle()
        {
            using ZddManager manager = new ZddManager(4);
            int[] identity = [0, 1, 2, 3];

            Assert.Equal(manager.Empty, manager.Empty.MapItems(identity));
            Assert.Equal(manager.Base, manager.Base.MapItems(identity));
        }

        // ---- Count 不変 ----

        [Fact]
        public void CountIsPreservedByMapItems()
        {
            const int VariableCount = FamilyCases.DefaultVariableCount;

            using ZddManager manager = new ZddManager(VariableCount);
            Random random = new Random(271828);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 30, seed: 271828))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);
                int[] support = zdd.Support();
                int[] itemMap = BuildMonotonicItemMap(VariableCount, support, random);

                Assert.Equal(zdd.Count, zdd.MapItems(itemMap).Count);
            }
        }

        // ---- 深い ZDD（スタックオーバーフロー回帰テスト） ----

        [Fact]
        public void DeepDiagramsDoNotOverflowTheStack()
        {
            // 変数 10 万のうち偶数番だけを使う鎖。素直な再帰実装ならここで StackOverflowException
            // になり、.NET では catch できずプロセスごと落ちる（docs/PLAN.md §4.5）。
            // support を全変数の半分に絞るのは、全変数を使うと support 上で単調な itemMap が
            // 恒等写像しかなくなり（有限全順序の自己同型は恒等写像のみ）、実際の張り替えを
            // 確かめられなくなるため。
            const int VariableCount = 100_000;

            using ZddManager manager = new ZddManager(VariableCount);

            // { {i} : i は偶数, 0 <= i < VariableCount }。
            Zdd singletons = manager.Empty;
            for (int item = VariableCount - 2; item >= 0; item -= 2)
            {
                singletons = manager.CreateNode(item, singletons, manager.Base);
            }

            Assert.Equal((long)(VariableCount / 2), singletons.NodeCount);

            // 隣り合う偶奇のペアを入れ替える写像（i XOR 1）。support（偶数）だけを見れば
            // 1, 3, 5, ... と狭義単調増加なので高速経路が使える。
            int[] itemMap = new int[VariableCount];
            for (int i = 0; i < VariableCount; i++)
            {
                itemMap[i] = i ^ 1;
            }

            Zdd mapped = singletons.MapItems(itemMap);

            Assert.Equal((long)(VariableCount / 2), mapped.NodeCount);
            Assert.Equal(singletons.Count, mapped.Count);

            int[] expectedSupport = Enumerable.Range(0, VariableCount / 2).Select(i => 2 * i + 1).ToArray();
            Assert.Equal(expectedSupport, mapped.Support());
        }

        // ---- 引数の検査 ----

        [Fact]
        public void WrongLengthItemMapIsRejected()
        {
            using ZddManager manager = new ZddManager(4);
            Zdd zdd = manager.Singleton(0);

            ArgumentException ex = Assert.Throws<ArgumentException>(() => zdd.MapItems(0, 1, 2));
            Assert.Equal("itemMap", ex.ParamName);
        }

        [Fact]
        public void NonInjectiveItemMapIsRejected()
        {
            using ZddManager manager = new ZddManager(4);
            Zdd zdd = manager.Singleton(0);

            // item 1 と item 2 が両方とも新 item 0 に写る。
            ArgumentException ex = Assert.Throws<ArgumentException>(() => zdd.MapItems(0, 0, 2, 3));
            Assert.Equal("itemMap", ex.ParamName);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        [InlineData(int.MaxValue)]
        public void AnOutOfRangeItemMapEntryIsRejected(int badTarget)
        {
            using ZddManager manager = new ZddManager(4);
            Zdd zdd = manager.Singleton(0);

            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => zdd.MapItems(badTarget, 1, 2, 3));
            Assert.Equal("itemMap", ex.ParamName);
        }

        [Fact]
        public void ANonMonotonicItemMapOnTheSupportIsRejected()
        {
            using ZddManager manager = new ZddManager(4);

            // support = {0, 2}（両方の item が実際に族の分岐に使われている）。
            Zdd zdd = ZddFamilies.Build(manager, [0], [2]);
            Assert.Equal([0, 2], zdd.Support());

            // item 0 と item 2 を入れ替えると、support 上で 0 -> 2, 2 -> 0 となり単調増加でない。
            Assert.Throws<NotSupportedException>(() => zdd.MapItems(2, 1, 0, 3));
        }

        [Fact]
        public void ANonMonotonicItemMapOutsideTheSupportIsAccepted()
        {
            using ZddManager manager = new ZddManager(4);

            // support = {0, 2}；item 1 と item 3 は使われていない。
            Zdd zdd = ZddFamilies.Build(manager, [0], [2]);

            // item 1 <-> item 3 を入れ替えても support 上の順序（0 < 2, どちらも自分自身へ）は
            // 崩れないので受理される。
            int[] itemMap = [0, 3, 2, 1];
            Zdd mapped = zdd.MapItems(itemMap);

            FamilyAssert.AssertSameFamily(
                "MapItems(support 外だけ入れ替え)",
                mapped,
                ZddFamilies.ToBruteForce(zdd).MapItems(itemMap));
        }

        [Fact]
        public void ADefaultHandleHasNoOperations()
        {
            Zdd none = default;

            Assert.Throws<InvalidOperationException>(() => none.MapItems(0, 1, 2, 3));
        }

        [Fact]
        public void AFamilyFromAnotherManagerIsRejected()
        {
            using ZddManager one = new ZddManager(4);
            using ZddManager other = new ZddManager(4);

            Zdd foreign = other.Singleton(0);

            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.MapItems(foreign, [1, 0, 2, 3])).ParamName);
        }

        [Fact]
        public void OperationsOnADisposedManagerThrow()
        {
            ZddManager manager = new ZddManager(4);
            Zdd zdd = ZddFamilies.Build(manager, [0], [2]);
            manager.Dispose();

            // 恒等写像でない張り替えでなければテーブルに触れないので、必ず非恒等写像を使う。
            Assert.Throws<ObjectDisposedException>(() => zdd.MapItems(1, 0, 3, 2));
        }

        // ---- ホットパスのアロケーション ----

#if DEBUG
        [Fact(Skip = "Debug ビルドでは Debug.Assert のメッセージ生成そのものがアロケートするため、Release でのみ測る。")]
#else
        [Fact]
#endif
        public void TheHotPathDoesNotAllocate()
        {
            const int VariableCount = BruteForceFamily.MaxVariableCount;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd zdd = ZddFamilies.Build(manager, BruteForceFamily.RandomSets(VariableCount, 40, new Random(8484)));
            int[] support = zdd.Support();
            int[] itemMap = BuildMonotonicItemMap(VariableCount, support, new Random(1));

            // 先に JIT を通し、作業領域を出揃わせる。測るのは MapItemsOperation.Apply 自体
            // （O(ノード数) のボトムアップ再構築）のアロケーションで、Zdd.MapItems のエントリ
            // ポイント全体（itemMap の検査や CollectSupport）は対象外——検査は O(VariableCount) の
            // 一度きりの費用で、ノード数に比例するホットパスとは別物（PowerSetOf/Flip も同様に
            // 呼び出しごとの検査は許容している）。
            _ = MapItemsOperation.Apply(manager, zdd.Id, itemMap);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                _ = MapItemsOperation.Apply(manager, zdd.Id, itemMap);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0L, after - before);
        }

        // ---- 照合の本体 ----

        /// <summary>族を ZDD に組み立て、いくつかの単調 itemMap（恒等写像を含む）で照合する。</summary>
        private static void AssertMapItemsMatchesNaive(
            ZddManager manager,
            BruteForceFamily family,
            Random random,
            int itemMapsPerFamily)
        {
            Zdd zdd = ZddFamilies.Build(manager, family);

            // 組み立て自体が壊れていたら、以降の照合は何も言っていないことになる。
            FamilyAssert.AssertSameFamily("the family builder", zdd, family);

            int[] support = zdd.Support();

            for (int i = 0; i < itemMapsPerFamily; i++)
            {
                int[] itemMap = BuildMonotonicItemMap(manager.VariableCount, support, random);
                FamilyAssert.AssertSameFamily($"MapItems #{i}", zdd.MapItems(itemMap), family.MapItems(itemMap), family);
            }
        }

        /// <summary>
        /// <paramref name="support"/> 上で狭義単調増加になるよう、ランダムな置換 <c>itemMap</c> を作る。
        /// </summary>
        /// <remarks>
        /// support に属する item には、昇順に並べた新 item をランダムに <paramref name="support"/>.Length
        /// 個選んで昇順のまま割り当てる（順序を保ったまま選ぶので support 上は自動的に単調増加になる）。
        /// support に属さない item には残りの新 item を任意の順で割り当てる（B17: support 外は
        /// 検査されないので、崩れていても構わない）。
        /// </remarks>
        private static int[] BuildMonotonicItemMap(int variableCount, int[] support, Random random)
        {
            int[] allNewItems = Enumerable.Range(0, variableCount).ToArray();
            Shuffle(allNewItems, random);

            int[] supportTargets = allNewItems.Take(support.Length).ToArray();
            Array.Sort(supportTargets);

            int[] remainingTargets = allNewItems.Skip(support.Length).ToArray();
            Shuffle(remainingTargets, random);

            int[] remainingOldItems = Enumerable.Range(0, variableCount).Except(support).ToArray();

            int[] itemMap = new int[variableCount];

            for (int i = 0; i < support.Length; i++)
            {
                itemMap[support[i]] = supportTargets[i];
            }

            for (int i = 0; i < remainingOldItems.Length; i++)
            {
                itemMap[remainingOldItems[i]] = remainingTargets[i];
            }

            return itemMap;
        }

        private static void Shuffle(int[] values, Random random)
        {
            for (int i = values.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }
    }
}

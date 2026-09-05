using System;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="Zdd.MapItemsTo"/> / <see cref="Zdd.TransferTo"/> の検証（M6-5, issue #140):
    /// 項目写像を別の <see cref="ZddManager"/> 上に複製する経路と、その恒等写像特化版。
    /// 同一マネージャ内の写像そのものの正しさ（高速経路・一般経路）は <c>MapItemsTests</c> の担当。
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="BruteForceFamily.MapItemsTo"/>。写像先マネージャの変数の個数は
    /// ソースより多くてよい（このテストでは主に <c>+3</c> 変数の target で確かめる）。
    /// <see cref="Zdd.MapItemsTo"/> 自体は <c>target.VariableCount &gt;= source</c> を明示的には
    /// 要求しないが、単射写像である以上（鳩の巣原理により）実質的にそれを満たさなければ
    /// 検証で必ず弾かれる——ソースの変数が 0 個の自明なケースを除く。<see cref="TransferTo"/> が
    /// この不等号を明示的にチェックして分かりやすいエラーにしているのはそのため。
    /// </remarks>
    public class MapItemsToTests
    {
        // ---- 総当たり照合（マネージャ間） ----

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(FamilyCases.DefaultVariableCount)]
        [InlineData(FamilyCases.ExhaustiveVariableLimit)]
        public void RandomFamiliesMatchTheNaiveImplementationAcrossManagers(int variableCount)
        {
            const int ExtraTargetVariables = 3;

            using ZddManager source = new ZddManager(variableCount);
            using ZddManager target = new ZddManager(variableCount + ExtraTargetVariables);
            Random random = new Random(31415 + variableCount);

            foreach (BruteForceFamily family in
                FamilyCases.RandomFamilies(variableCount, 30, seed: 31415 + variableCount))
            {
                Zdd zdd = ZddFamilies.Build(source, family);
                FamilyAssert.AssertSameFamily("the family builder", zdd, family);

                for (int i = 0; i < 3; i++)
                {
                    int[] itemMap = BuildRandomInjectiveMap(variableCount, target.VariableCount, random);

                    Zdd transferred = zdd.MapItemsTo(target, itemMap);
                    Assert.Equal(target, transferred.Manager);

                    FamilyAssert.AssertSameFamily(
                        $"MapItemsTo(target) #{i}",
                        transferred,
                        family.MapItemsTo(target.VariableCount, itemMap),
                        family);
                }
            }
        }

        [Fact]
        public void MapItemsToTheOwnManagerIsExactlyMapItems()
        {
            const int VariableCount = FamilyCases.DefaultVariableCount;

            using ZddManager manager = new ZddManager(VariableCount);
            Random random = new Random(20260906);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 20260906))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);
                int[] itemMap = BuildRandomInjectiveMap(VariableCount, VariableCount, random);

                Assert.Equal(zdd.MapItems(itemMap), zdd.MapItemsTo(manager, itemMap));
            }
        }

        // ---- 恒等写像でもマネージャが違えば複製する ----

        [Fact]
        public void CrossManagerIdentityMapStillBuildsANewFamily()
        {
            const int VariableCount = 6;

            using ZddManager source = new ZddManager(VariableCount);
            using ZddManager target = new ZddManager(VariableCount);

            Zdd zdd = ZddFamilies.Build(source, BruteForceFamily.Random(VariableCount, 0.4, seed: 7));
            int[] identity = Enumerable.Range(0, VariableCount).ToArray();

            Zdd transferred = zdd.MapItemsTo(target, identity);

            Assert.Equal(target, transferred.Manager);
            Assert.NotEqual(source, transferred.Manager);
            FamilyAssert.AssertSameFamily(transferred, ZddFamilies.ToBruteForce(zdd));
        }

        // ---- TransferTo ----

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public void TransferToCopiesTheFamilyUnchanged(int extraTargetVariables)
        {
            const int VariableCount = FamilyCases.DefaultVariableCount;

            using ZddManager source = new ZddManager(VariableCount);
            using ZddManager target = new ZddManager(VariableCount + extraTargetVariables);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 27182 + extraTargetVariables))
            {
                Zdd zdd = ZddFamilies.Build(source, family);
                Zdd transferred = zdd.TransferTo(target);

                Assert.Equal(target, transferred.Manager);
                Assert.Equal(zdd.Count, transferred.Count);

                int[] identity = Enumerable.Range(0, VariableCount).ToArray();
                FamilyAssert.AssertSameFamily(
                    "TransferTo",
                    transferred,
                    family.MapItemsTo(target.VariableCount, identity),
                    family);
            }
        }

        [Fact]
        public void TransferToItsOwnManagerReturnsTheSameHandle()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);
            Zdd zdd = ZddFamilies.Build(manager, BruteForceFamily.Random(VariableCount, 0.4, seed: 11));
            long nodeCountBefore = manager.NodeCount;

            // TransferTo(target) is MapItemsTo(target, identity); when target is this family's own
            // manager, that's exactly the same-manager identity short-circuit MapItems relies on.
            Zdd transferred = zdd.TransferTo(manager);

            Assert.Equal(zdd, transferred);
            Assert.Equal(nodeCountBefore, manager.NodeCount);
        }

        [Fact]
        public void TransferToUsesTheFastPathSoNodeCountMatches()
        {
            const int VariableCount = 100_000;

            using ZddManager source = new ZddManager(VariableCount);
            using ZddManager target = new ZddManager(VariableCount);

            // { {i} : i は偶数, 0 <= i < VariableCount }。TransferTo は恒等写像 = 単調なので、
            // 高速経路（O(ノード数)）を通り、変数 10 万でもスタックオーバーフローしない
            // （M6-4 の回帰テストと同じ構造で、マネージャ間版であることを確かめる）。
            Zdd singletons = source.Empty;
            for (int item = VariableCount - 2; item >= 0; item -= 2)
            {
                singletons = source.CreateNode(item, singletons, source.Base);
            }

            Zdd transferred = singletons.TransferTo(target);

            Assert.Equal(singletons.NodeCount, transferred.NodeCount);
            Assert.Equal(singletons.Count, transferred.Count);
            Assert.Equal(singletons.Support(), transferred.Support());
        }

        [Fact]
        public void TransferToATargetWithFewerVariablesThrows()
        {
            using ZddManager source = new ZddManager(5);
            using ZddManager target = new ZddManager(4);

            Zdd zdd = source.Singleton(0);

            ArgumentException ex = Assert.Throws<ArgumentException>(() => zdd.TransferTo(target));
            Assert.Equal("target", ex.ParamName);
        }

        [Fact]
        public void TransferToATargetWithTheExactSameVariableCountSucceeds()
        {
            using ZddManager source = new ZddManager(5);
            using ZddManager target = new ZddManager(5);

            Zdd zdd = ZddFamilies.Build(source, BruteForceFamily.Random(5, 0.5, seed: 3));
            Zdd transferred = zdd.TransferTo(target);

            FamilyAssert.AssertSameFamily(transferred, ZddFamilies.ToBruteForce(zdd));
        }

        [Fact]
        public void TransferToANullTargetThrows()
        {
            using ZddManager source = new ZddManager(4);
            Zdd zdd = source.Singleton(0);

            Assert.Equal("target", Assert.Throws<ArgumentNullException>(() => zdd.TransferTo(null!)).ParamName);
        }

        // ---- 引数の検査 ----

        [Fact]
        public void MapItemsToANullTargetThrows()
        {
            using ZddManager source = new ZddManager(4);
            Zdd zdd = source.Singleton(0);

            Assert.Equal(
                "target",
                Assert.Throws<ArgumentNullException>(() => zdd.MapItemsTo(null!, 0, 1, 2, 3)).ParamName);
        }

        [Fact]
        public void MapItemsToWithAnOutOfRangeEntryForTheTargetIsRejected()
        {
            using ZddManager source = new ZddManager(4);
            using ZddManager target = new ZddManager(3);

            Zdd zdd = source.Singleton(0);

            // target は 3 変数（0..2）しかないのに、itemMap は item 3 を要求している。
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => zdd.MapItemsTo(target, 0, 1, 2, 3));
            Assert.Equal("itemMap", ex.ParamName);
        }

        [Fact]
        public void MapItemsToWithANonInjectiveItemMapIsRejected()
        {
            using ZddManager source = new ZddManager(4);
            using ZddManager target = new ZddManager(6);

            Zdd zdd = source.Singleton(0);

            ArgumentException ex = Assert.Throws<ArgumentException>(() => zdd.MapItemsTo(target, 0, 0, 2, 3));
            Assert.Equal("itemMap", ex.ParamName);
        }

        [Fact]
        public void MapItemsToWithAWrongLengthItemMapIsRejected()
        {
            using ZddManager source = new ZddManager(4);
            using ZddManager target = new ZddManager(4);

            Zdd zdd = source.Singleton(0);

            ArgumentException ex = Assert.Throws<ArgumentException>(() => zdd.MapItemsTo(target, 0, 1, 2));
            Assert.Equal("itemMap", ex.ParamName);
        }

        [Fact]
        public void MapItemsToAFamilyFromAnotherManagerIsRejected()
        {
            using ZddManager one = new ZddManager(4);
            using ZddManager other = new ZddManager(4);
            using ZddManager target = new ZddManager(4);

            Zdd foreign = other.Singleton(0);

            Assert.Equal(
                "f",
                Assert.Throws<ArgumentException>(() => one.MapItemsTo(foreign, target, [1, 0, 2, 3])).ParamName);
        }

        [Fact]
        public void ADefaultHandleHasNoMapItemsToOrTransferTo()
        {
            using ZddManager target = new ZddManager(4);
            Zdd none = default;

            Assert.Throws<InvalidOperationException>(() => none.MapItemsTo(target, 0, 1, 2, 3));
            Assert.Throws<InvalidOperationException>(() => none.TransferTo(target));
        }

        [Fact]
        public void MapItemsToWithADisposedSourceThrows()
        {
            ZddManager source = new ZddManager(4);
            using ZddManager target = new ZddManager(4);

            Zdd zdd = source.Singleton(0);
            source.Dispose();

            Assert.Throws<ObjectDisposedException>(() => zdd.MapItemsTo(target, 0, 1, 2, 3));
        }

        [Fact]
        public void MapItemsToWithADisposedTargetThrows()
        {
            using ZddManager source = new ZddManager(4);
            ZddManager target = new ZddManager(4);

            Zdd zdd = source.Singleton(0);
            target.Dispose();

            Assert.Throws<ObjectDisposedException>(() => zdd.MapItemsTo(target, 0, 1, 2, 3));
        }

        [Fact]
        public void TransferToWithADisposedTargetThrows()
        {
            using ZddManager source = new ZddManager(4);
            ZddManager target = new ZddManager(4);

            Zdd zdd = source.Singleton(0);
            target.Dispose();

            Assert.Throws<ObjectDisposedException>(() => zdd.TransferTo(target));
        }

        // ---- ヘルパ ----

        /// <summary>0..sourceVariableCount - 1 から 0..targetVariableCount - 1 への、ランダムな単射写像を作る。</summary>
        private static int[] BuildRandomInjectiveMap(int sourceVariableCount, int targetVariableCount, Random random)
        {
            int[] targets = Enumerable.Range(0, targetVariableCount).ToArray();
            Shuffle(targets, random);
            return targets.Take(sourceVariableCount).ToArray();
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

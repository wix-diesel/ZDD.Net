using System;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="ZddManager.Collect()"/> / <see cref="ZddManager.RootSet"/>: mark &amp; sweep,
    /// compaction, id remap (issue #55 / docs/PLAN.md &#167;4.4).
    /// </summary>
    public class NodeGarbageCollectorTests
    {
        // ---- RootSet に登録したハンドルは GC 後も正しく動く ----

        [Fact]
        public void RootSetHandlesSurviveCollectionWithTheSameCountAndSets()
        {
            using ZddManager manager = new ZddManager(8);

            Zdd kept = manager.Empty;
            for (int item = 0; item < 8; item += 2)
            {
                kept |= manager.Singleton(item);
            }

            manager.RootSet.Add(kept);

            // 根から辿れないゴミを大量に作る。
            for (int item = 0; item < 8; item++)
            {
                _ = manager.Singleton(item) & manager.Singleton((item + 1) % 8);
            }

            long countBefore = kept.NodeCount;
            BigInteger cardinalityBefore = kept.Count;
            int[][] setsBefore = kept.Sets().Select(s => s.ToArray()).ToArray();

            manager.Collect();

            Zdd revived = manager.RootSet[0];

            Assert.Equal(countBefore, revived.NodeCount);
            Assert.Equal(cardinalityBefore, revived.Count);

            int[][] setsAfter = revived.Sets().Select(s => s.ToArray()).ToArray();
            Assert.Equal(setsBefore.Length, setsAfter.Length);
            foreach (int[] expected in setsBefore)
            {
                Assert.Contains(setsAfter, actual => actual.SequenceEqual(expected));
            }
        }

        [Fact]
        public void MultipleRootsAllSurviveInRegistrationOrder()
        {
            using ZddManager manager = new ZddManager(6);

            Zdd a = manager.Singleton(0);
            Zdd b = manager.Singleton(1) | manager.Singleton(2);
            Zdd c = manager.Singleton(3).Complement();

            manager.RootSet.Add(a);
            manager.RootSet.Add(b);
            manager.RootSet.Add(c);

            BigInteger[] countsBefore = { a.Count, b.Count, c.Count };

            manager.Collect();

            Assert.Equal(3, manager.RootSet.Count);
            Assert.Equal(countsBefore[0], manager.RootSet[0].Count);
            Assert.Equal(countsBefore[1], manager.RootSet[1].Count);
            Assert.Equal(countsBefore[2], manager.RootSet[2].Count);
        }

        [Fact]
        public void OldLocalCopyOfARegisteredHandleIsStillStaleAfterCollection()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd kept = manager.Singleton(0);
            manager.RootSet.Add(kept);

            manager.Collect();

            // 登録していても、GC 前に取得したローカル変数そのものは古い世代のまま。
            Assert.Throws<ZddCollectedException>(() => kept.NodeCount);

            // 生きているのは族そのもの: RootSet から読み直せば動く。
            Zdd revived = manager.RootSet[0];
            Assert.Equal(1L, revived.NodeCount);
        }

        // ---- メモリが実際に減る ----

        [Fact]
        public void CollectionReducesNodeCountAndTableCapacity()
        {
            using ZddManager manager = new ZddManager(64);

            Zdd keep = manager.Singleton(0);
            manager.RootSet.Add(keep);

            for (int item = 1; item < 64; item++)
            {
                for (int other = 0; other < item; other++)
                {
                    _ = manager.Singleton(item) | manager.Singleton(other);
                }
            }

            ZddStatistics before = manager.GetStatistics();
            Assert.True(before.NodeCount > 100, "the setup should have built plenty of garbage nodes");

            manager.Collect();

            ZddStatistics after = manager.GetStatistics();

            Assert.Equal(1L, after.NodeCount);
            Assert.True(after.NodeCount < before.NodeCount);
            Assert.True(
                after.NodeTableCapacity < before.NodeTableCapacity,
                $"expected capacity to shrink from {before.NodeTableCapacity}, but it is {after.NodeTableCapacity}");

            Assert.Equal(1L, after.CollectionCount);
            Assert.Equal(before.NodeCount - 1, after.LastCollectionRemovedNodeCount);
            Assert.InRange(after.LastCollectionReductionRatio, 0.0, 1.0);
            Assert.True(after.LastCollectionReductionRatio > 0.9);
        }

        [Fact]
        public void CollectingWithAnEmptyRootSetReclaimsEverything()
        {
            using ZddManager manager = new ZddManager(8);

            _ = manager.Singleton(0) | manager.Singleton(1);
            Assert.True(manager.NodeCount > 0);

            manager.Collect();

            Assert.Equal(0L, manager.NodeCount);
        }

        // ---- 正準性が保たれる ----

        [Fact]
        public void RebuildingTheSameFamilyAfterCollectionReusesTheSameNode()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd s0 = manager.Singleton(0);
            Zdd s1 = manager.Singleton(1);
            Zdd a = s0 | s1;

            // 演算のオペランド自身も生かしておく。さもないと再構築時に Singleton(0) を
            // 一から作り直す羽目になり、ノード数が変わってしまう（族としては同じでも）。
            manager.RootSet.Add(s0);
            manager.RootSet.Add(s1);
            manager.RootSet.Add(a);

            _ = manager.Singleton(2) | manager.Singleton(3);

            manager.Collect();

            long nodeCountAfterCollect = manager.NodeCount;

            // 演算キャッシュは GC で空になっているので、これは一意化表からの再構築になる。
            Zdd rebuilt = manager.RootSet[0] | manager.RootSet[1];

            Assert.Equal(manager.RootSet[2], rebuilt);
            Assert.Equal(nodeCountAfterCollect, manager.NodeCount);
        }

        [Fact]
        public void AStaleHandleNeverEqualsAFreshHandleThatReusesItsOldId()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd stale = manager.Singleton(0);

            // 何も RootSet に登録しないので、次の Collect で全滅する。
            manager.Collect();

            // 新しく作った族が、たまたま同じ ID を再利用しても、世代が違うので別物である。
            Zdd fresh = manager.Singleton(0);

            Assert.NotEqual(stale, fresh);
        }

        // ---- 未登録のハンドルを GC 後に使うと明確な例外 ----

        [Fact]
        public void AnUnregisteredHandleThrowsZddCollectedAfterCollection()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd kept = manager.Singleton(0);
            manager.RootSet.Add(kept);

            Zdd stale = manager.Singleton(1);

            manager.Collect();

            Assert.Throws<ZddCollectedException>(() => stale.NodeCount);
            Assert.Throws<ZddCollectedException>(() => stale.Support());
            Assert.Throws<ZddCollectedException>(() => _ = stale.Manager);
        }

        [Fact]
        public void UsingAStaleOperandInABinaryOperationThrows()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd kept = manager.Singleton(0);
            manager.RootSet.Add(kept);

            Zdd stale = manager.Singleton(1);

            manager.Collect();

            Zdd fresh = manager.RootSet[0];

            Assert.Throws<ZddCollectedException>(() => fresh.Union(stale));
        }

        [Fact]
        public void TerminalsStayValidAcrossCollectionWithoutRegistration()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd empty = manager.Empty;
            Zdd @base = manager.Base;

            manager.Collect();

            Assert.True(empty.IsEmpty);
            Assert.True(@base.IsBase);
            Assert.Equal(0L, empty.NodeCount);
            Assert.Equal(0L, @base.NodeCount);
        }

        // ---- 到達可能なノードは誤って回収されない ----

        [Fact]
        public void EveryReachableNodeSurvivesCollection()
        {
            using ZddManager manager = new ZddManager(10);

            Zdd family = manager.Base;
            for (int item = 9; item >= 0; item--)
            {
                family = manager.CreateNode(item, lo: manager.Empty, hi: family);
            }

            manager.RootSet.Add(family);
            long before = family.NodeCount;

            manager.Collect();

            Assert.Equal(before, manager.RootSet[0].NodeCount);
            Assert.Equal(before, manager.NodeCount);
        }

        [Fact]
        public void SharedSubgraphsBetweenTwoRootsAreNotDoubleFreedOrLost()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd shared = manager.Singleton(2);
            Zdd a = manager.CreateNode(0, lo: manager.Empty, hi: shared);
            Zdd b = manager.CreateNode(1, lo: manager.Empty, hi: shared);

            manager.RootSet.Add(a);
            manager.RootSet.Add(b);

            manager.Collect();

            // a・b それぞれ自分のノード + shared の 2 個が辿れる。
            Assert.Equal(2L, manager.RootSet[0].NodeCount);
            Assert.Equal(2L, manager.RootSet[1].NodeCount);

            // shared は a と b の両方から辿れるので、二重に確保されず 1 個だけ（合計 4 ではなく 3）。
            Assert.Equal(3L, manager.NodeCount);
        }

        // ---- 深い ZDD で mark がスタックオーバーフローしない ----

        [Fact]
        public void CollectDoesNotOverflowOnADeepChain()
        {
            const int VariableCount = 100_000;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd family = manager.Base;
            for (int item = VariableCount - 1; item >= 0; item--)
            {
                family = manager.CreateNode(item, lo: manager.Empty, hi: family);
            }

            manager.RootSet.Add(family);

            manager.Collect();

            Assert.Equal((long)VariableCount, manager.RootSet[0].NodeCount);
        }

        // ---- 繰り返し GC が正しく動く ----

        [Fact]
        public void RepeatedCollectionsKeepWorking()
        {
            using ZddManager manager = new ZddManager(8);

            Zdd kept = manager.Singleton(0);
            manager.RootSet.Add(kept);

            for (int i = 0; i < 5; i++)
            {
                _ = manager.Singleton(1) | manager.Singleton(2);

                manager.Collect();

                Assert.Equal(1L, manager.RootSet[0].NodeCount);
                Assert.Equal(1L, manager.NodeCount);
            }

            Assert.Equal(5L, manager.GetStatistics().CollectionCount);
        }

        // ---- Collect(params Zdd[] roots) ----

        [Fact]
        public void CollectWithExplicitRootsRegistersThemFirst()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd a = manager.Singleton(0);
            Zdd b = manager.Singleton(1);

            _ = manager.Singleton(2);

            manager.Collect(a, b);

            Assert.Equal(2, manager.RootSet.Count);
            Assert.Equal(2L, manager.NodeCount);
        }

        [Fact]
        public void CollectWithExplicitRootsRejectsADefaultHandle()
        {
            using ZddManager manager = new ZddManager(4);

            Assert.Throws<ArgumentException>(() => manager.Collect(manager.Singleton(0), default));
        }

        [Fact]
        public void CollectRejectsANullRootsArray()
        {
            using ZddManager manager = new ZddManager(4);

            Assert.Throws<ArgumentNullException>(() => manager.Collect((Zdd[])null!));
        }

        // ---- GC しない限り既存の振る舞いは変わらない ----

        [Fact]
        public void ANewManagerNeverCollectedReportsZeroedGcStatistics()
        {
            using ZddManager manager = new ZddManager(4);

            ZddStatistics statistics = manager.GetStatistics();

            Assert.Equal(0L, statistics.CollectionCount);
            Assert.Equal(0L, statistics.LastCollectionRemovedNodeCount);
            Assert.Equal(0.0, statistics.LastCollectionReductionRatio);
            Assert.Equal(TimeSpan.Zero, statistics.LastCollectionDuration);
        }

        [Fact]
        public void PeakNodeCountStaysAtItsHighWaterMarkAfterCollectionShrinksCount()
        {
            using ZddManager manager = new ZddManager(8);

            Zdd family = manager.Empty;
            for (int item = 0; item < 8; item++)
            {
                family |= manager.Singleton(item);
            }

            long peakBefore = manager.GetStatistics().PeakNodeCount;

            manager.RootSet.Add(manager.Singleton(0));
            manager.Collect();

            ZddStatistics after = manager.GetStatistics();

            Assert.Equal(1L, after.NodeCount);
            Assert.Equal(peakBefore, after.PeakNodeCount);
            Assert.True(after.PeakNodeCount > after.NodeCount);
        }

        // ---- 2^U のキャッシュ（PowerSetRoot）の再マップ ----

        [Fact]
        public void ComplementRecomputesCorrectlyWhenThePowerSetRootDoesNotSurviveCollection()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd universe = manager.Empty.Complement();
            BigInteger expected = universe.Count;

            // universe を RootSet に登録しないので、GC で回収される。
            manager.Collect();

            Zdd universeAgain = manager.Empty.Complement();

            Assert.Equal(expected, universeAgain.Count);
            Assert.Equal(BigInteger.Pow(2, 4), universeAgain.Count);
        }

        [Fact]
        public void ComplementStaysCorrectWhenThePowerSetRootSurvivesCollection()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd universe = manager.Empty.Complement();
            manager.RootSet.Add(universe);

            manager.Collect();

            Zdd universeAgain = manager.Empty.Complement();

            Assert.Equal(BigInteger.Pow(2, 4), universeAgain.Count);
        }

        // ---- RootSet 自体の振る舞い ----

        [Fact]
        public void AddIsIdempotentAndPreservesRegistrationOrder()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd a = manager.Singleton(0);
            Zdd b = manager.Singleton(1);

            manager.RootSet.Add(a);
            manager.RootSet.Add(b);
            manager.RootSet.Add(a);

            Assert.Equal(2, manager.RootSet.Count);
            Assert.Equal(a, manager.RootSet[0]);
            Assert.Equal(b, manager.RootSet[1]);
        }

        [Fact]
        public void AddIgnoresTerminals()
        {
            using ZddManager manager = new ZddManager(4);

            manager.RootSet.Add(manager.Empty);
            manager.RootSet.Add(manager.Base);

            Assert.Empty(manager.RootSet);
            Assert.True(manager.RootSet.Contains(manager.Empty));
            Assert.True(manager.RootSet.Contains(manager.Base));
        }

        [Fact]
        public void AddRejectsADefaultHandle()
        {
            using ZddManager manager = new ZddManager(4);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => manager.RootSet.Add(default));
            Assert.Equal("zdd", exception.ParamName);
        }

        [Fact]
        public void AddRejectsAHandleFromAnotherManager()
        {
            using ZddManager manager = new ZddManager(4);
            using ZddManager other = new ZddManager(4);

            Assert.Throws<ArgumentException>(() => manager.RootSet.Add(other.Singleton(0)));
        }

        [Fact]
        public void RemoveAndContainsReflectRegistration()
        {
            using ZddManager manager = new ZddManager(4);
            Zdd a = manager.Singleton(0);

            Assert.False(manager.RootSet.Contains(a));

            manager.RootSet.Add(a);
            Assert.True(manager.RootSet.Contains(a));

            Assert.True(manager.RootSet.Remove(a));
            Assert.False(manager.RootSet.Contains(a));
            Assert.False(manager.RootSet.Remove(a));
        }

        [Fact]
        public void ClearEmptiesTheSet()
        {
            using ZddManager manager = new ZddManager(4);

            manager.RootSet.Add(manager.Singleton(0));
            manager.RootSet.Add(manager.Singleton(1));

            manager.RootSet.Clear();

            Assert.Empty(manager.RootSet);
        }

        [Fact]
        public void IndexerThrowsForAnOutOfRangeIndex()
        {
            using ZddManager manager = new ZddManager(4);
            manager.RootSet.Add(manager.Singleton(0));

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => manager.RootSet[1]);
            Assert.Equal("index", exception.ParamName);
        }

        [Fact]
        public void EnumeratingYieldsRootsInRegistrationOrder()
        {
            using ZddManager manager = new ZddManager(4);
            Zdd a = manager.Singleton(0);
            Zdd b = manager.Singleton(1);

            manager.RootSet.Add(a);
            manager.RootSet.Add(b);

            Assert.Equal(new[] { a, b }, manager.RootSet.ToArray());
        }

        // ---- 破棄後 ----

        [Fact]
        public void CollectAndRootSetThrowAfterDispose()
        {
            ZddManager manager = new ZddManager(4);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => manager.RootSet);
            Assert.Throws<ObjectDisposedException>(() => manager.Collect());
        }
    }
}

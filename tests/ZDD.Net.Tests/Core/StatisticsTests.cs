using System;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="ZddManager.GetStatistics"/> と <see cref="ZddStatistics"/> の検証。
    /// </summary>
    /// <remarks>
    /// 統計は答の正しさには関わらないので、確かめるのは<b>値が期待どおりに増減すること</b>と、
    /// 派生値（率）が定義どおりであることに絞る。絶対値を固定すると、演算の実装が
    /// ノードを 1 個多く作った・少なく作ったというだけでテストが落ちるようになる。
    /// </remarks>
    public class StatisticsTests
    {
        [Fact]
        public void ANewManagerHasNothingInItsTables()
        {
            using ZddManager manager = new ZddManager(8);

            ZddStatistics statistics = manager.GetStatistics();

            Assert.Equal(0L, statistics.NodeCount);
            Assert.Equal(0L, statistics.PeakNodeCount);
            Assert.Equal(0L, statistics.UniqueTableCollisions);
            Assert.Equal(0L, statistics.CacheLookups);
            Assert.Equal(0L, statistics.CacheHits);
            Assert.Equal(0L, statistics.CacheMisses);
            Assert.Equal(0L, statistics.CacheOverwrites);

            // 一度も引いていなければ率は 0（0 除算にしない）。
            Assert.Equal(0.0, statistics.CacheHitRate);
            Assert.Equal(0.0, statistics.UniqueTableLoadFactor);
        }

        [Fact]
        public void TheReportedCapacitiesAreTheOnesTheOptionsAskedFor()
        {
            ZddManagerOptions options = new ZddManagerOptions
            {
                InitialNodeCapacity = 64,
                InitialUniqueTableCapacity = 256,
                InitialCacheCapacity = 32,
                MaxCacheCapacity = 512,
            };

            using ZddManager manager = new ZddManager(4, options);

            ZddStatistics statistics = manager.GetStatistics();

            // ノード表は予約済みの終端 2 個を同じ配列に持つ。
            Assert.Equal(66L, statistics.NodeTableCapacity);
            Assert.Equal(256, statistics.UniqueTableCapacity);
            Assert.Equal(32, statistics.CacheCapacity);
            Assert.Equal(512, statistics.MaxCacheCapacity);
        }

        [Fact]
        public void TheNodeCountFollowsTheNodesActuallyCreated()
        {
            using ZddManager manager = new ZddManager(8);

            Assert.Equal(0L, manager.GetStatistics().NodeCount);

            _ = manager.Singleton(0);
            Assert.Equal(1L, manager.GetStatistics().NodeCount);

            // 同じ族をもう一度作っても、一意化表が同じノードを返すので増えない。
            _ = manager.Singleton(0);
            Assert.Equal(1L, manager.GetStatistics().NodeCount);

            _ = manager.Singleton(1);
            Assert.Equal(2L, manager.GetStatistics().NodeCount);

            Assert.Equal(manager.NodeCount, manager.GetStatistics().NodeCount);
        }

        [Fact]
        public void ThePeakFollowsTheNodeCountWhileNothingIsReclaimed()
        {
            using ZddManager manager = new ZddManager(16);

            Zdd family = manager.Empty;
            for (int item = 0; item < 16; item++)
            {
                family |= manager.Singleton(item);

                ZddStatistics statistics = manager.GetStatistics();

                // ノードを手放す手段はまだ無い（ノード GC は M5-3）ので、ピークは現在値に等しい。
                Assert.Equal(statistics.NodeCount, statistics.PeakNodeCount);
            }
        }

        [Fact]
        public void TheNodeTableGrowsWhenItRunsOut()
        {
            ZddManagerOptions options = new ZddManagerOptions { InitialNodeCapacity = 4 };

            using ZddManager manager = new ZddManager(64, options);

            Assert.Equal(6L, manager.GetStatistics().NodeTableCapacity);

            Zdd family = manager.Empty;
            for (int item = 0; item < 64; item++)
            {
                family |= manager.Singleton(item);
            }

            ZddStatistics statistics = manager.GetStatistics();

            Assert.True(statistics.NodeTableCapacity > 6L, "the node table should have grown");
            Assert.InRange(statistics.NodeTableLoadFactor, 0.0, 1.0);

            // 使用率は「終端 2 個を含めた使用済みスロット ÷ 容量」。
            Assert.Equal(
                (statistics.NodeCount + 2.0) / statistics.NodeTableCapacity,
                statistics.NodeTableLoadFactor,
                12);
        }

        [Fact]
        public void TheUniqueTableLoadFactorStaysUnderTheGrowThreshold()
        {
            using ZddManager manager = new ZddManager(1024);

            Zdd family = manager.Empty;
            for (int item = 0; item < 1024; item++)
            {
                family |= manager.Singleton(item);
            }

            ZddStatistics statistics = manager.GetStatistics();

            Assert.True(statistics.NodeCount > 1024L, "the family should have taken more than one node per item");
            Assert.Equal(
                (double)statistics.NodeCount / statistics.UniqueTableCapacity,
                statistics.UniqueTableLoadFactor,
                12);

            // 負荷率が 70% を超えたら倍化されるので、落ち着いた先は必ずそれ以下になる。
            Assert.InRange(
                statistics.UniqueTableLoadFactor,
                0.0,
                UniqueTable.MaxLoadFactorPercent / 100.0);
        }

        [Fact]
        public void ACrampedUniqueTableCollides()
        {
            // スロットを 4 個しか持たない表に、倍化しながらノードを詰め込む。
            ZddManagerOptions options = new ZddManagerOptions { InitialUniqueTableCapacity = 1 };

            using ZddManager manager = new ZddManager(256, options);

            Zdd family = manager.Empty;
            for (int item = 0; item < 256; item++)
            {
                family |= manager.Singleton(item);
            }

            Assert.True(
                manager.GetStatistics().UniqueTableCollisions > 0L,
                "linear probing should have stepped over occupied slots at least once");
        }

        [Fact]
        public void TheCacheCountsEveryLookupAndHitsOnTheSecondCall()
        {
            using ZddManager manager = new ZddManager(12);

            Zdd left = manager.Empty;
            Zdd right = manager.Empty;
            for (int item = 0; item < 12; item++)
            {
                if (item % 2 == 0)
                {
                    left |= manager.Singleton(item);
                }
                else
                {
                    right |= manager.Singleton(item);
                }
            }

            ZddStatistics before = manager.GetStatistics();
            Zdd union = left | right;
            ZddStatistics afterFirst = manager.GetStatistics();

            Assert.True(afterFirst.CacheLookups > before.CacheLookups, "the union should have consulted the cache");
            Assert.Equal(afterFirst.CacheLookups - afterFirst.CacheHits, afterFirst.CacheMisses);

            // 2 回目はすべて覚えている（根の 1 引きで済む）ので、ヒットが増える。
            Assert.Equal(union, left | right);

            ZddStatistics afterSecond = manager.GetStatistics();

            Assert.True(afterSecond.CacheHits > afterFirst.CacheHits, "the repeated union should have hit the cache");
            Assert.Equal((double)afterSecond.CacheHits / afterSecond.CacheLookups, afterSecond.CacheHitRate, 12);
        }

        [Fact]
        public void ATinyCacheOverwritesItsEntries()
        {
            // エントリ 1 個しかない表。別の部分問題を書くたびに先客が消える。
            ZddManagerOptions options = new ZddManagerOptions
            {
                InitialCacheCapacity = 1,
                MaxCacheCapacity = 1,
            };

            using ZddManager manager = new ZddManager(64, options);

            Zdd family = manager.Empty;
            for (int item = 0; item < 64; item++)
            {
                family |= manager.Singleton(item);
            }

            _ = family.Complement();

            ZddStatistics statistics = manager.GetStatistics();

            Assert.Equal(1, statistics.CacheCapacity);
            Assert.True(statistics.CacheOverwrites > 0L, "a one-entry cache must have overwritten entries");
        }

        [Fact]
        public void TheCacheIsNotGrownBeyondItsMaximum()
        {
            ZddManagerOptions options = new ZddManagerOptions { MaxCacheCapacity = 0 };

            using ZddManager manager = new ZddManager(32, options);

            Zdd family = manager.Empty;
            for (int item = 0; item < 32; item++)
            {
                family |= manager.Singleton(item);
            }

            ZddStatistics statistics = manager.GetStatistics();

            Assert.Equal(0, statistics.CacheCapacity);
            Assert.Equal(0, statistics.MaxCacheCapacity);

            // 無効な表でも引きには数えられ、すべて外れる。
            Assert.True(statistics.CacheLookups > 0L);
            Assert.Equal(0L, statistics.CacheHits);
            Assert.Equal(statistics.CacheLookups, statistics.CacheMisses);
        }

        // ---- 写しであること ----

        [Fact]
        public void AStatisticsValueDoesNotChangeAfterItIsTaken()
        {
            using ZddManager manager = new ZddManager(8);

            ZddStatistics before = manager.GetStatistics();

            Zdd family = manager.Singleton(0) | manager.Singleton(1);
            Assert.False(family.IsEmpty);

            ZddStatistics after = manager.GetStatistics();

            // 先に取った値は、あとで族を作っても取った時点のままである。
            Assert.Equal(0L, before.NodeCount);
            Assert.Equal(0L, before.CacheLookups);

            Assert.True(after.NodeCount > 0L);
            Assert.NotEqual(before, after);
        }

        [Fact]
        public void EqualStatisticsCompareEqual()
        {
            using ZddManager manager = new ZddManager(8);

            ZddStatistics one = manager.GetStatistics();
            ZddStatistics other = manager.GetStatistics();

            Assert.True(one == other);
            Assert.False(one != other);
            Assert.True(one.Equals((object)other));
            Assert.Equal(one.GetHashCode(), other.GetHashCode());
        }

        [Fact]
        public void TheSummaryMentionsEveryTable()
        {
            using ZddManager manager = new ZddManager(8);

            _ = manager.Singleton(0) | manager.Singleton(1);

            string summary = manager.GetStatistics().ToString();

            Assert.Contains("nodes", summary, StringComparison.Ordinal);
            Assert.Contains("node table", summary, StringComparison.Ordinal);
            Assert.Contains("unique table", summary, StringComparison.Ordinal);
            Assert.Contains("operation cache", summary, StringComparison.Ordinal);
            Assert.Contains("cache lookups", summary, StringComparison.Ordinal);
            Assert.Contains("cache overwrites", summary, StringComparison.Ordinal);
        }

        // ---- 破棄後 ----

        [Fact]
        public void GetStatisticsThrowsAfterDispose()
        {
            ZddManager manager = new ZddManager(4);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => manager.GetStatistics());
        }
    }
}

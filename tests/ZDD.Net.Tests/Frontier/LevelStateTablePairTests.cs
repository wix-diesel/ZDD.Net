using System;
using Xunit;
using ZDD.Net.Frontier;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// レベルごとの生成・破棄（<see cref="LevelStateTablePair{TTable}"/>）。
    /// </summary>
    /// <remarks>
    /// フロンティア法のピークメモリを決めるのがここ。表をレベルごとに作れば、
    /// 深さに比例してメモリが伸びてしまう。2 枚を回して使い続けるのが約束で、
    /// 「2 枚しか無いこと」自体をテストにしておく。
    /// </remarks>
    public class LevelStateTablePairTests
    {
        [Fact]
        public void AdvanceMakesTheFilledLevelCurrentAndRecyclesTheFinishedOne()
        {
            using LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>> pair = NewPair();

            StructLevelStateTable<LiveOnlySpec, PairState> first = pair.Current;
            StructLevelStateTable<LiveOnlySpec, PairState> second = pair.Next;

            pair.Advance();

            Assert.Same(second, pair.Current);
            Assert.Same(first, pair.Next);

            pair.Advance();

            Assert.Same(first, pair.Current);
            Assert.Same(second, pair.Next);
        }

        [Fact]
        public void AdvanceClearsTheRecycledTableButKeepsTheOneJustFilled()
        {
            using LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>> pair = NewPair();

            pair.Current.GetOrAdd(new PairState(1, 0));
            pair.Next.GetOrAdd(new PairState(2, 0));
            pair.Next.GetOrAdd(new PairState(3, 0));

            pair.Advance();

            Assert.Equal(2, pair.Current.Count);
            Assert.Equal(0, pair.Next.Count);
            Assert.Equal(2, pair.Current[0].Live);
        }

        /// <summary>
        /// 何レベル進んでも表は 2 枚のままで、レベル数に比例したメモリ増加は起きない。
        /// </summary>
        [Fact]
        public void WalkingManyLevelsReusesTheSameTwoTables()
        {
            const int LevelCount = 200;
            const int WidthPerLevel = 500;

            using LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>> pair = NewPair();

            // 2 レベル分の表を先に広げてから測る（倍化そのものは配列を借りるので確保が出る）。
            FillLevel(pair, WidthPerLevel);
            FillLevel(pair, WidthPerLevel);

            StructLevelStateTable<LiveOnlySpec, PairState> first = pair.Current;
            StructLevelStateTable<LiveOnlySpec, PairState> second = pair.Next;
            int capacity = pair.Current.Capacity;
            int strangers = 0;

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int level = 0; level < LevelCount; level++)
            {
                if (!ReferenceEquals(pair.Current, first) && !ReferenceEquals(pair.Current, second))
                {
                    strangers++;
                }

                FillLevel(pair, WidthPerLevel);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, strangers);
            Assert.Equal(capacity, pair.Current.Capacity);
            Assert.Equal(capacity, pair.Next.Capacity);
            Assert.Equal(0L, allocated);
        }

        [Fact]
        public void TheStatisticsCoverBothLevels()
        {
            using LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>> pair = NewPair();

            FillLevel(pair, 10);
            FillLevel(pair, 4);
            FillLevel(pair, 7);

            Assert.Equal(21L, pair.TotalRegistered);
            Assert.Equal(10, pair.PeakWidth);
            Assert.Equal(pair.Current.Collisions + pair.Next.Collisions, pair.Collisions);
        }

        [Fact]
        public void TheTwoLevelsMustBeTwoDifferentTables()
        {
            StructLevelStateTable<LiveOnlySpec, PairState> table = NewTable();

            Assert.Throws<ArgumentException>(
                () => new LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>>(table, table));
            Assert.Throws<ArgumentNullException>(
                () => new LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>>(table, null!));

            table.Dispose();
        }

        [Fact]
        public void DisposingThePairDisposesBothTables()
        {
            LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>> pair = NewPair();
            StructLevelStateTable<LiveOnlySpec, PairState> current = pair.Current;
            StructLevelStateTable<LiveOnlySpec, PairState> next = pair.Next;

            pair.Dispose();

            Assert.Throws<ObjectDisposedException>(() => current.GetOrAdd(new PairState(1, 0)));
            Assert.Throws<ObjectDisposedException>(() => next.GetOrAdd(new PairState(1, 0)));
        }

        private static LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>> NewPair() =>
            new LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>>(NewTable(), NewTable());

        private static StructLevelStateTable<LiveOnlySpec, PairState> NewTable() =>
            new StructLevelStateTable<LiveOnlySpec, PairState>(default, LevelStateTable.MinimumCapacity);

        /// <summary>次のレベルを埋めて 1 段進む。構築器（M2-3）がやることの骨だけ。</summary>
        private static void FillLevel(
            LevelStateTablePair<StructLevelStateTable<LiveOnlySpec, PairState>> pair,
            int width)
        {
            for (int i = 0; i < width; i++)
            {
                pair.Next.GetOrAdd(new PairState(i, 0));
            }

            pair.Advance();
        }
    }
}

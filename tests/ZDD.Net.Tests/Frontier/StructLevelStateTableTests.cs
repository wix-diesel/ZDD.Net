using System;
using System.Collections.Generic;
using Xunit;
using ZDD.Net.Frontier;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// 固定長 struct 状態の状態表（<see cref="StructLevelStateTable{TSpec, TState}"/>）。
    /// </summary>
    /// <remarks>
    /// フロンティア法が指数爆発を避けられるのは「同じ状態になった枝を 1 本にまとめる」からで、
    /// その重複除去がこの表の仕事。ここが壊れると探索結果が黙って間違う（多い側にも少ない側にも
    /// ずれる）ので、重複除去の正しさを最優先で見る。
    /// </remarks>
    public class StructLevelStateTableTests
    {
        [Fact]
        public void TheSameStateAlwaysGetsTheSameIndex()
        {
            using StructLevelStateTable<LiveOnlySpec, PairState> table = NewTable();

            int first = table.GetOrAdd(new PairState(7, 0));

            Assert.Equal(first, table.GetOrAdd(new PairState(7, 0)));
            Assert.Equal(first, table.GetOrAdd(new PairState(7, 0)));
            Assert.Equal(1, table.Count);
        }

        [Fact]
        public void DifferentStatesGetDifferentIndexes()
        {
            using StructLevelStateTable<LiveOnlySpec, PairState> table = NewTable();

            int[] indexes = new int[16];
            for (int i = 0; i < indexes.Length; i++)
            {
                indexes[i] = table.GetOrAdd(new PairState(i, 0));
            }

            Assert.Equal(new HashSet<int>(indexes).Count, indexes.Length);
            Assert.Equal(indexes.Length, table.Count);
        }

        /// <summary>
        /// 等価判定が無視する差（<see cref="PairState.Stale"/>）しかない状態は、同じ 1 本にまとまる。
        /// </summary>
        [Fact]
        public void StatesTheSpecCallsEqualShareOneIndex()
        {
            using StructLevelStateTable<LiveOnlySpec, PairState> table = NewTable();

            int index = table.GetOrAdd(new PairState(3, 100));

            Assert.Equal(index, table.GetOrAdd(new PairState(3, 200)));
            Assert.Equal(1, table.Count);
        }

        /// <summary>逆に、等価判定が false なら、ハッシュが同じでも別の index になる。</summary>
        [Fact]
        public void StatesTheSpecCallsDifferentStaySeparateEvenWhenTheHashesCollide()
        {
            using StructLevelStateTable<ConstantHashSpec, PairState> table =
                new StructLevelStateTable<ConstantHashSpec, PairState>(default, LevelStateTable.MinimumCapacity);

            const int StateCount = 500;
            int[] indexes = new int[StateCount];

            for (int i = 0; i < StateCount; i++)
            {
                indexes[i] = table.GetOrAdd(new PairState(i / 2, i % 2));
            }

            Assert.Equal(StateCount, table.Count);
            Assert.Equal(StateCount, new HashSet<int>(indexes).Count);

            // 全部が同じハッシュなので、線形探索の連鎖を必ず踏んでいるはず。
            Assert.True(table.Collisions > 0, "Colliding states must have been probed past one another.");

            for (int i = 0; i < StateCount; i++)
            {
                Assert.Equal(indexes[i], table.GetOrAdd(new PairState(i / 2, i % 2)));
            }
        }

        [Fact]
        public void TheStoredStateCanBeReadBackByIndex()
        {
            using StructLevelStateTable<LiveOnlySpec, PairState> table = NewTable();

            int index = table.GetOrAdd(new PairState(11, 22));

            Assert.Equal(11, table[index].Live);
            Assert.Equal(22, table[index].Stale);
        }

        /// <summary>
        /// 倍化が何度も走る規模でも、先に配った index が同じ状態を指し続ける。
        /// </summary>
        /// <remarks>
        /// 倍化はスロット配列を作り直す。index を配り直す実装にしてしまうと、
        /// 既に子として参照されている一時ノードが別の状態を指すようになり、
        /// 出来上がる ZDD が静かに壊れる。ここが M2-3 以降の前提になる。
        /// </remarks>
        [Fact]
        public void ManyStatesSurviveRepeatedGrowth()
        {
            const int StateCount = 100_000;

            using StructLevelStateTable<LiveOnlySpec, PairState> table =
                new StructLevelStateTable<LiveOnlySpec, PairState>(default, LevelStateTable.MinimumCapacity);

            int[] indexes = new int[StateCount];
            for (int i = 0; i < StateCount; i++)
            {
                indexes[i] = table.GetOrAdd(new PairState(i, 0));
            }

            // 最小容量から 10 万件なので、倍化は 15 回以上走っている。
            Assert.True(
                table.Capacity >= StateCount,
                $"The table must have grown past {StateCount} slots, but has {table.Capacity}.");
            Assert.Equal(StateCount, table.Count);

            for (int i = 0; i < StateCount; i++)
            {
                Assert.Equal(i, table[indexes[i]].Live);
                Assert.Equal(indexes[i], table.GetOrAdd(new PairState(i, 0)));
            }

            Assert.Equal(StateCount, table.Count);
        }

        /// <summary>レベルを跨ぐ統計（登録数・ピーク幅・衝突回数）は <c>Clear</c> で消えない。</summary>
        [Fact]
        public void ClearRestartsTheLevelButKeepsTheStatisticsAndTheBuffers()
        {
            using StructLevelStateTable<LiveOnlySpec, PairState> table = NewTable();

            for (int i = 0; i < 10; i++)
            {
                table.GetOrAdd(new PairState(i, 0));
            }

            int capacity = table.Capacity;
            table.Clear();

            Assert.Equal(0, table.Count);
            Assert.Equal(capacity, table.Capacity);
            Assert.Equal(10, table.PeakWidth);
            Assert.Equal(10L, table.TotalRegistered);

            // 前のレベルの状態が残っていれば、ここで 0 以外が返る。
            Assert.Equal(0, table.GetOrAdd(new PairState(5, 0)));
            Assert.Equal(1, table.Count);
            Assert.Equal(11L, table.TotalRegistered);
            Assert.Equal(10, table.PeakWidth);
        }

        /// <summary>
        /// 登録の hot path でアロケーションが出ないこと。
        /// </summary>
        /// <remarks>
        /// 状態 1 個ごとに走る道なので、ここで確保が出ると GC がフロンティア構築の律速になる。
        /// 倍化そのものは配列を借りるので、容量を先に取ってから測る。
        /// </remarks>
        [Fact]
        public void RegisteringStatesDoesNotAllocate()
        {
            using StructLevelStateTable<LiveOnlySpec, PairState> table =
                new StructLevelStateTable<LiveOnlySpec, PairState>(default, 4096);

            // JIT とプールの初回確保を測定から外す。
            Fill(table, 1000);
            table.Clear();

            long before = GC.GetAllocatedBytesForCurrentThread();
            Fill(table, 1000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(1000, table.Count);
            Assert.Equal(0L, allocated);
        }

        [Fact]
        public void AnIndexOutsideTheCurrentLevelIsRejected()
        {
            using StructLevelStateTable<LiveOnlySpec, PairState> table = NewTable();

            table.GetOrAdd(new PairState(1, 0));

            Assert.Throws<ArgumentOutOfRangeException>(() => table[1].Live);
            Assert.Throws<ArgumentOutOfRangeException>(() => table[-1].Live);
        }

        [Fact]
        public void ADisposedTableIsNotUsableAgain()
        {
            StructLevelStateTable<LiveOnlySpec, PairState> table = NewTable();
            table.Dispose();

            Assert.Throws<ObjectDisposedException>(() => table.GetOrAdd(new PairState(1, 0)));
            Assert.Throws<ObjectDisposedException>(table.Clear);

            // 二重解放でプールに同じ配列を 2 回返さない。
            table.Dispose();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(LevelStateTable.MaxCapacity + 1)]
        public void AnUnusableInitialCapacityIsRejected(int initialCapacity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new StructLevelStateTable<LiveOnlySpec, PairState>(default, initialCapacity));
        }

        private static StructLevelStateTable<LiveOnlySpec, PairState> NewTable() =>
            new StructLevelStateTable<LiveOnlySpec, PairState>(default, 64);

        private static void Fill(StructLevelStateTable<LiveOnlySpec, PairState> table, int stateCount)
        {
            for (int i = 0; i < stateCount; i++)
            {
                table.GetOrAdd(new PairState(i, 0));
            }
        }
    }
}

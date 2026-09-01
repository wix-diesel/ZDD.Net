using System;
using System.Collections.Generic;
using Xunit;
using ZDD.Net.Frontier;
using ZDD.Net.Internal;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// 可変長配列状態の状態表（<see cref="ArrayLevelStateTable"/>）。
    /// </summary>
    /// <remarks>
    /// <see cref="IArrayDdSpec"/> の状態は要素ごとの比較で等価と決まっているので、
    /// この表はスペックを呼ばない。長さは実行時に決まる（mate 配列はフロンティアの大きさで決まる）ため、
    /// 長さの違うスペックそれぞれで正しく動くことを見る。
    /// </remarks>
    public class ArrayLevelStateTableTests
    {
        [Fact]
        public void TheSameStateAlwaysGetsTheSameIndex()
        {
            using ArrayLevelStateTable table = new ArrayLevelStateTable(arrayLength: 3, initialCapacity: 64);

            int first = table.GetOrAdd(new[] { 1, 2, 3 });

            Assert.Equal(first, table.GetOrAdd(new[] { 1, 2, 3 }));
            Assert.Equal(1, table.Count);
        }

        [Fact]
        public void StatesDifferingInAnySlotGetDifferentIndexes()
        {
            using ArrayLevelStateTable table = new ArrayLevelStateTable(arrayLength: 3, initialCapacity: 64);

            int[] indexes =
            {
                table.GetOrAdd(new[] { 0, 0, 0 }),
                table.GetOrAdd(new[] { 1, 0, 0 }),
                table.GetOrAdd(new[] { 0, 1, 0 }),
                table.GetOrAdd(new[] { 0, 0, 1 }),
            };

            Assert.Equal(indexes.Length, new HashSet<int>(indexes).Count);
            Assert.Equal(indexes.Length, table.Count);
        }

        /// <summary>長さの違うスペックが、それぞれ独立に正しく動く。</summary>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(17)]
        public void EveryArrayLengthRoundTripsItsStates(int arrayLength)
        {
            const int StateCount = 300;

            using ArrayLevelStateTable table = new ArrayLevelStateTable(arrayLength, LevelStateTable.MinimumCapacity);

            int[] state = new int[arrayLength];
            int[] indexes = new int[StateCount];

            for (int i = 0; i < StateCount; i++)
            {
                WriteState(state, i);
                indexes[i] = table.GetOrAdd(state);
            }

            Assert.Equal(StateCount, table.Count);
            Assert.Equal(StateCount, new HashSet<int>(indexes).Count);
            Assert.Equal(arrayLength, table.ArrayLength);

            for (int i = 0; i < StateCount; i++)
            {
                WriteState(state, i);
                Assert.Equal(indexes[i], table.GetOrAdd(state));
                Assert.True(table[indexes[i]].SequenceEqual(state), $"State {i} must be readable back unchanged.");
            }
        }

        /// <summary>長さの違う表を同時に使っても、互いの状態が混ざらない。</summary>
        [Fact]
        public void TablesOfDifferentArrayLengthsAreIndependent()
        {
            using ArrayLevelStateTable two = new ArrayLevelStateTable(arrayLength: 2, initialCapacity: 64);
            using ArrayLevelStateTable four = new ArrayLevelStateTable(arrayLength: 4, initialCapacity: 64);

            Assert.Equal(0, two.GetOrAdd(new[] { 1, 2 }));
            Assert.Equal(0, four.GetOrAdd(new[] { 1, 2, 0, 0 }));
            Assert.Equal(1, four.GetOrAdd(new[] { 1, 2, 0, 1 }));
            Assert.Equal(1, two.Count);
            Assert.Equal(2, four.Count);
        }

        [Fact]
        public void AStateOfTheWrongLengthIsRejected()
        {
            using ArrayLevelStateTable table = new ArrayLevelStateTable(arrayLength: 3, initialCapacity: 64);

            Assert.Throws<ArgumentException>(() => table.GetOrAdd(new[] { 1, 2 }));
            Assert.Throws<ArgumentException>(() => table.GetOrAdd(new[] { 1, 2, 3, 4 }));
        }

        /// <summary>
        /// ハッシュが実際に衝突する 2 つの状態を探し当てて、それでも別扱いになることを確かめる。
        /// </summary>
        /// <remarks>
        /// 表は「ハッシュが同じ」で候補を絞ってから中身を比べる。中身の比較を落とすと、
        /// この 2 つが 1 本にまとまり、探索が枝を取りこぼす。
        /// 衝突は作り込めないので、32bit のハッシュが一致する組を探索して見つける（誕生日の問題）。
        /// </remarks>
        [Fact]
        public void StatesWhoseHashesCollideStaySeparate()
        {
            const int ArrayLength = 4;

            (int[] Left, int[] Right) collision = FindHashCollision(ArrayLength);

            using ArrayLevelStateTable table = new ArrayLevelStateTable(ArrayLength, initialCapacity: 64);

            int left = table.GetOrAdd(collision.Left);
            int right = table.GetOrAdd(collision.Right);

            Assert.NotEqual(left, right);
            Assert.Equal(2, table.Count);
            Assert.True(table.Collisions > 0, "The second state must have probed past the first.");
            Assert.Equal(left, table.GetOrAdd(collision.Left));
            Assert.Equal(right, table.GetOrAdd(collision.Right));
            Assert.True(table[left].SequenceEqual(collision.Left));
            Assert.True(table[right].SequenceEqual(collision.Right));
        }

        /// <summary>倍化が何度も走る規模でも、先に配った index が同じ状態を指し続ける。</summary>
        [Fact]
        public void ManyStatesSurviveRepeatedGrowth()
        {
            const int StateCount = 100_000;
            const int ArrayLength = 6;

            using ArrayLevelStateTable table = new ArrayLevelStateTable(ArrayLength, LevelStateTable.MinimumCapacity);

            int[] state = new int[ArrayLength];
            int[] indexes = new int[StateCount];

            for (int i = 0; i < StateCount; i++)
            {
                WriteState(state, i);
                indexes[i] = table.GetOrAdd(state);
            }

            Assert.True(
                table.Capacity >= StateCount,
                $"The table must have grown past {StateCount} slots, but has {table.Capacity}.");
            Assert.Equal(StateCount, table.Count);

            for (int i = 0; i < StateCount; i++)
            {
                WriteState(state, i);
                Assert.True(table[indexes[i]].SequenceEqual(state), $"State {i} must survive the grows unchanged.");
                Assert.Equal(indexes[i], table.GetOrAdd(state));
            }

            Assert.Equal(StateCount, table.Count);
        }

        [Fact]
        public void ClearRestartsTheLevelButKeepsTheBuffers()
        {
            using ArrayLevelStateTable table = new ArrayLevelStateTable(arrayLength: 2, initialCapacity: 64);

            for (int i = 0; i < 10; i++)
            {
                table.GetOrAdd(new[] { i, 0 });
            }

            int capacity = table.Capacity;
            table.Clear();

            Assert.Equal(0, table.Count);
            Assert.Equal(capacity, table.Capacity);
            Assert.Equal(10, table.PeakWidth);
            Assert.Equal(10L, table.TotalRegistered);
            Assert.Equal(0, table.GetOrAdd(new[] { 5, 0 }));
        }

        /// <summary>登録の hot path でアロケーションが出ないこと。</summary>
        [Fact]
        public void RegisteringStatesDoesNotAllocate()
        {
            const int ArrayLength = 8;

            using ArrayLevelStateTable table = new ArrayLevelStateTable(ArrayLength, initialCapacity: 4096);
            int[] state = new int[ArrayLength];

            // JIT とプールの初回確保を測定から外す。
            Fill(table, state, 1000);
            table.Clear();

            long before = GC.GetAllocatedBytesForCurrentThread();
            Fill(table, state, 1000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(1000, table.Count);
            Assert.Equal(0L, allocated);
        }

        [Fact]
        public void AnIndexOutsideTheCurrentLevelIsRejected()
        {
            using ArrayLevelStateTable table = new ArrayLevelStateTable(arrayLength: 2, initialCapacity: 64);

            table.GetOrAdd(new[] { 1, 1 });

            Assert.Throws<ArgumentOutOfRangeException>(() => table[1].Length);
            Assert.Throws<ArgumentOutOfRangeException>(() => table[-1].Length);
        }

        [Fact]
        public void ADisposedTableIsNotUsableAgain()
        {
            ArrayLevelStateTable table = new ArrayLevelStateTable(arrayLength: 2, initialCapacity: 64);
            table.Dispose();

            Assert.Throws<ObjectDisposedException>(() => table.GetOrAdd(new[] { 1, 1 }));
            Assert.Throws<ObjectDisposedException>(table.Clear);

            table.Dispose();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void AnUnusableArrayLengthIsRejected(int arrayLength)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArrayLevelStateTable(arrayLength, initialCapacity: 64));
        }

        /// <summary>状態 <paramref name="seed"/> 番目を、全スロットが効くように書き込む。</summary>
        private static void WriteState(int[] state, int seed)
        {
            for (int i = 0; i < state.Length; i++)
            {
                state[i] = (seed >> i) & 1;
            }

            state[seed % state.Length] = seed;
        }

        private static void Fill(ArrayLevelStateTable table, int[] state, int stateCount)
        {
            for (int i = 0; i < stateCount; i++)
            {
                WriteState(state, i);
                table.GetOrAdd(state);
            }
        }

        /// <summary>ハッシュ（表が使うのと同じ計算）が一致する、内容の違う 2 状態を探す。</summary>
        private static (int[] Left, int[] Right) FindHashCollision(int arrayLength)
        {
            const int Candidates = 400_000;

            Dictionary<int, int> seenBySeed = new Dictionary<int, int>(Candidates);
            int[] state = new int[arrayLength];

            for (int seed = 0; seed < Candidates; seed++)
            {
                WriteState(state, seed);
                int hash = (int)Hashing.Combine((ReadOnlySpan<int>)state);

                if (seenBySeed.TryGetValue(hash, out int other))
                {
                    int[] left = new int[arrayLength];
                    WriteState(left, other);
                    return (left, (int[])state.Clone());
                }

                seenBySeed.Add(hash, seed);
            }

            Assert.Fail($"No hash collision found among {Candidates} states of {arrayLength} slot(s).");
            return default;
        }
    }
}

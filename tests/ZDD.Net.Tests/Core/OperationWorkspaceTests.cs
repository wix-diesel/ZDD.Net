using System;
using System.Collections.Generic;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    public class OperationWorkspaceTests
    {
        // ---- 作業スタック ----

        [Fact]
        public void ANewWorkspaceIsEmpty()
        {
            OperationWorkspace work = new OperationWorkspace();

            Assert.True(work.IsEmpty);
            Assert.Equal(0, work.Depth);
            Assert.Equal(0, work.ResultCount);
            Assert.False(work.TryPop(out _));
        }

        [Fact]
        public void ItemsComeBackInReverseOrder()
        {
            OperationWorkspace work = new OperationWorkspace();

            work.PushVisit(10);
            work.PushVisit(20);
            work.PushVisit(30);

            Assert.Equal(3, work.Depth);

            Assert.True(work.TryPop(out long first));
            Assert.True(work.TryPop(out long second));
            Assert.True(work.TryPop(out long third));

            Assert.Equal(30, first);
            Assert.Equal(20, second);
            Assert.Equal(10, third);
            Assert.True(work.IsEmpty);
        }

        [Fact]
        public void ACombineItemCarriesTheSameKeyWithAMark()
        {
            OperationWorkspace work = new OperationWorkspace();

            work.PushCombine(0);
            work.PushCombine(4242);
            work.PushVisit(4242);

            Assert.True(work.TryPop(out long visit));
            Assert.False(OperationWorkspace.IsCombine(visit));
            Assert.Equal(4242, OperationWorkspace.KeyOf(visit));

            Assert.True(work.TryPop(out long combine));
            Assert.True(OperationWorkspace.IsCombine(combine));
            Assert.Equal(4242, OperationWorkspace.KeyOf(combine));

            // キー 0 も印を付けられる（0 の反転は -1 で、負の値になる）。
            Assert.True(work.TryPop(out long zero));
            Assert.True(OperationWorkspace.IsCombine(zero));
            Assert.Equal(0, OperationWorkspace.KeyOf(zero));
        }

        [Fact]
        public void TheStackGrowsBeyondItsInitialCapacity()
        {
            OperationWorkspace work = new OperationWorkspace(1, OperationWorkspace.MinimumResultCapacity);

            const int Count = 10_000;
            for (int i = 0; i < Count; i++)
            {
                work.PushVisit(i);
            }

            Assert.Equal(Count, work.Depth);
            Assert.True(work.StackCapacity >= Count);

            for (int i = Count - 1; i >= 0; i--)
            {
                Assert.True(work.TryPop(out long entry));
                Assert.Equal(i, entry);
            }
        }

        // ---- 途中結果表 ----

        [Fact]
        public void AResultCanBeReadBack()
        {
            OperationWorkspace work = new OperationWorkspace();

            Assert.False(work.HasResult(7));
            Assert.False(work.TryGetResult(7, out int missing));
            Assert.Equal(NodeTable.Bottom, missing);

            work.SetResult(7, 123);

            Assert.True(work.HasResult(7));
            Assert.True(work.TryGetResult(7, out int found));
            Assert.Equal(123, found);
            Assert.Equal(1, work.ResultCount);
        }

        [Fact]
        public void TheBottomTerminalIsAValidResult()
        {
            OperationWorkspace work = new OperationWorkspace();

            work.SetResult(0, NodeTable.Bottom);

            // 「⊥ が答」と「まだ答が出ていない」は別物でなければならない。
            Assert.True(work.TryGetResult(0, out int result));
            Assert.Equal(NodeTable.Bottom, result);
            Assert.False(work.HasResult(1));
        }

        [Fact]
        public void SettingTheSameKeyTwiceOverwritesInPlace()
        {
            OperationWorkspace work = new OperationWorkspace();

            work.SetResult(99, 1);
            work.SetResult(99, 2);

            Assert.Equal(1, work.ResultCount);
            Assert.True(work.TryGetResult(99, out int result));
            Assert.Equal(2, result);
        }

        [Fact]
        public void TheResultTableKeepsEveryEntryWhileItGrows()
        {
            OperationWorkspace work = new OperationWorkspace(1, OperationWorkspace.MinimumResultCapacity);

            const int Count = 50_000;
            for (int i = 0; i < Count; i++)
            {
                work.SetResult(i, i * 3);
            }

            Assert.Equal(Count, work.ResultCount);
            Assert.True(work.ResultCapacity >= Count);

            // lossy な演算キャッシュと違い、こちらは 1 件も落としてはならない。
            for (int i = 0; i < Count; i++)
            {
                Assert.True(work.TryGetResult(i, out int result));
                Assert.Equal(i * 3, result);
            }
        }

        [Fact]
        public void KeysThatSharePartsAreKeptApart()
        {
            OperationWorkspace work = new OperationWorkspace();

            // 二項演算が使う「2 つのノード ID を 32bit ずつ詰めたキー」の形。
            Dictionary<long, int> expected = new Dictionary<long, int>();
            for (int f = 0; f < 40; f++)
            {
                for (int g = 0; g < 40; g++)
                {
                    long key = ((long)f << 32) | (uint)g;
                    expected[key] = (f * 100) + g;
                    work.SetResult(key, expected[key]);
                }
            }

            Assert.Equal(expected.Count, work.ResultCount);

            foreach (KeyValuePair<long, int> entry in expected)
            {
                Assert.True(work.TryGetResult(entry.Key, out int result));
                Assert.Equal(entry.Value, result);
            }
        }

        // ---- 使い回し ----

        [Fact]
        public void ResetEmptiesBothTheStackAndTheResultTable()
        {
            OperationWorkspace work = new OperationWorkspace();

            for (int i = 0; i < 100; i++)
            {
                work.PushVisit(i);
                work.SetResult(i, i);
            }

            int stackCapacity = work.StackCapacity;
            int resultCapacity = work.ResultCapacity;

            work.Reset();

            Assert.True(work.IsEmpty);
            Assert.Equal(0, work.ResultCount);
            Assert.False(work.HasResult(0));
            Assert.False(work.HasResult(99));

            // 確保済みの配列は手放さない（使い回しても割り当てが起きないこと）。
            Assert.Equal(stackCapacity, work.StackCapacity);
            Assert.Equal(resultCapacity, work.ResultCapacity);
        }

        [Fact]
        public void ResultsDoNotLeakFromOneOperationToTheNext()
        {
            OperationWorkspace work = new OperationWorkspace(4, OperationWorkspace.MinimumResultCapacity);

            for (int round = 1; round <= 100; round++)
            {
                for (int key = 0; key < 50; key++)
                {
                    work.SetResult(key, (round * 1000) + key);
                }

                for (int key = 0; key < 50; key++)
                {
                    Assert.True(work.TryGetResult(key, out int result));
                    Assert.Equal((round * 1000) + key, result);
                }

                work.Reset();

                for (int key = 0; key < 50; key++)
                {
                    Assert.False(work.HasResult(key));
                }
            }
        }

        // ---- 引数の検査 ----

        [Theory]
        [InlineData(0, 4)]
        [InlineData(-1, 4)]
        [InlineData(4, 0)]
        [InlineData(4, -1)]
        public void TheConstructorRejectsNonPositiveCapacities(int stackCapacity, int resultCapacity)
        {
            Assert.ThrowsAny<ArgumentOutOfRangeException>(
                () => new OperationWorkspace(stackCapacity, resultCapacity));
        }

        [Fact]
        public void TheResultCapacityIsRoundedUpToAPowerOfTwo()
        {
            OperationWorkspace work = new OperationWorkspace(8, 100);

            Assert.Equal(128, work.ResultCapacity);
        }

        [Fact]
        public void TheResultCapacityHasAFloor()
        {
            OperationWorkspace work = new OperationWorkspace(1, 1);

            Assert.Equal(OperationWorkspace.MinimumResultCapacity, work.ResultCapacity);
        }
    }
}

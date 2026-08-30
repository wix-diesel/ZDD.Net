using System;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    public class NodeTableTests
    {
        [Fact]
        public void TerminalIdsAreReservedAndTableStartsEmpty()
        {
            NodeTable table = new NodeTable();

            Assert.Equal(0, NodeTable.Bottom);
            Assert.Equal(1, NodeTable.Top);
            Assert.Equal(2, NodeTable.FirstNodeId);
            Assert.Equal(0, table.Count);
            Assert.Equal(NodeTable.FirstNodeId, table.NextId);
            Assert.True(NodeTable.IsTerminal(NodeTable.Bottom));
            Assert.True(NodeTable.IsTerminal(NodeTable.Top));
            Assert.False(NodeTable.IsTerminal(NodeTable.FirstNodeId));
        }

        [Fact]
        public void TerminalsAreInitializedWithLevelZero()
        {
            NodeTable table = new NodeTable();

            foreach (int terminal in new[] { NodeTable.Bottom, NodeTable.Top })
            {
                ref ZddNode node = ref table[terminal];

                Assert.Equal(0, node.Level);
                Assert.Equal(NodeTable.Bottom, node.Lo);
                Assert.Equal(NodeTable.Bottom, node.Hi);
                Assert.Equal(NodeTable.NoNext, node.Next);
            }
        }

        [Fact]
        public void FirstAddReturnsIdTwoAndSubsequentIdsIncrease()
        {
            NodeTable table = new NodeTable();

            int first = table.Add(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            int second = table.Add(level: 2, lo: NodeTable.Top, hi: first);
            int third = table.Add(level: 3, lo: first, hi: second);

            Assert.Equal(2, first);
            Assert.Equal(3, second);
            Assert.Equal(4, third);
            Assert.Equal(3, table.Count);
            Assert.Equal(5, table.NextId);
        }

        [Fact]
        public void AddStoresTheGivenFields()
        {
            NodeTable table = new NodeTable();

            int child = table.Add(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            int id = table.Add(level: 7, lo: child, hi: NodeTable.Top);

            ref ZddNode node = ref table[id];

            Assert.Equal(7, node.Level);
            Assert.Equal(child, node.Lo);
            Assert.Equal(NodeTable.Top, node.Hi);
            Assert.Equal(NodeTable.NoNext, node.Next);
        }

        [Fact]
        public void IndexerReturnsWritableReferenceIntoTheTable()
        {
            NodeTable table = new NodeTable();
            int id = table.Add(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);

            table[id].Next = 42;

            Assert.Equal(42, table[id].Next);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(2)]
        [InlineData(3)]
        public void IndexerThrowsForIdsOutsideTheTable(int id)
        {
            NodeTable table = new NodeTable();

            // 終端しか無い状態なので、有効な ID は 0 と 1 のみ。
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => table[id].Level);

            Assert.Equal("id", exception.ParamName);
        }

        [Fact]
        public void IndexerThrowsForSlotsThatAreAllocatedButNotYetAdded()
        {
            NodeTable table = new NodeTable(initialCapacity: 64);

            Assert.Equal(64, table.Capacity);
            Assert.Throws<ArgumentOutOfRangeException>(() => table[table.NextId].Level);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void AddThrowsForNonPositiveLevel(int level)
        {
            NodeTable table = new NodeTable();

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => table.Add(level, NodeTable.Bottom, NodeTable.Top));

            Assert.Equal("level", exception.ParamName);
        }

        [Fact]
        public void AddThrowsWhenLoIsNotAnExistingId()
        {
            NodeTable table = new NodeTable();

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => table.Add(level: 1, lo: 2, hi: NodeTable.Top));

            Assert.Equal("lo", exception.ParamName);
        }

        [Fact]
        public void AddThrowsWhenHiIsNotAnExistingId()
        {
            NodeTable table = new NodeTable();

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => table.Add(level: 1, lo: NodeTable.Bottom, hi: 2));

            Assert.Equal("hi", exception.ParamName);
        }

        [Fact]
        public void AddThrowsWhenHiPointsToBottom()
        {
            NodeTable table = new NodeTable();

            // ゼロサプレス削減規則により、1-枝が ⊥ を指すノードは存在してはならない。
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => table.Add(level: 1, lo: NodeTable.Top, hi: NodeTable.Bottom));

            Assert.Equal("hi", exception.ParamName);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(-1)]
        public void ConstructorThrowsWhenInitialCapacityCannotHoldTheTerminals(int initialCapacity)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => new NodeTable(initialCapacity));

            Assert.Equal("initialCapacity", exception.ParamName);
        }

        [Fact]
        public void ConstructorThrowsWhenCapacityLimitIsBelowInitialCapacity()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => new NodeTable(initialCapacity: 16, capacityLimit: 15));

            Assert.Equal("capacityLimit", exception.ParamName);
        }

        [Fact]
        public void CapacityDoublesExactlyAtTheBoundary()
        {
            NodeTable table = new NodeTable(initialCapacity: 4);

            // 容量ちょうど（終端 2 個 + ノード 2 個）まではリサイズが起きない。
            table.Add(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            table.Add(level: 2, lo: NodeTable.Bottom, hi: NodeTable.Top);

            Assert.Equal(4, table.Capacity);
            Assert.Equal(4, table.NextId);

            // 容量 + 1 個目でちょうど倍化する。
            int id = table.Add(level: 3, lo: NodeTable.Bottom, hi: NodeTable.Top);

            Assert.Equal(8, table.Capacity);
            Assert.Equal(4, id);
            Assert.Equal(3, table.Count);
        }

        [Fact]
        public void ResizePreservesEveryPreviouslyAddedNode()
        {
            NodeTable table = new NodeTable(initialCapacity: 4);
            const int Count = 1000;

            for (int i = 0; i < Count; i++)
            {
                int expectedId = NodeTable.FirstNodeId + i;
                Assert.Equal(expectedId, table.Add(level: i + 1, lo: NodeTable.Bottom, hi: NodeTable.Top));
            }

            Assert.True(table.Capacity >= NodeTable.FirstNodeId + Count);

            for (int i = 0; i < Count; i++)
            {
                ref ZddNode node = ref table[NodeTable.FirstNodeId + i];

                Assert.Equal(i + 1, node.Level);
                Assert.Equal(NodeTable.Bottom, node.Lo);
                Assert.Equal(NodeTable.Top, node.Hi);
            }

            // 終端もコピーされていること。
            Assert.Equal(0, table[NodeTable.Bottom].Level);
            Assert.Equal(0, table[NodeTable.Top].Level);
        }

        [Fact]
        public void GrowthIsClampedToTheCapacityLimit()
        {
            NodeTable table = new NodeTable(initialCapacity: 4, capacityLimit: 6);

            for (int i = 0; i < 4; i++)
            {
                table.Add(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            }

            // 4 → 8 ではなく、上限の 6 で止まる。
            Assert.Equal(6, table.Capacity);
            Assert.Equal(4, table.Count);
        }

        [Fact]
        public void AddThrowsWhenTheNodeLimitIsReached()
        {
            // 実際に 2^31 ノードを確保することはできないので、内部の上限を差し替えて検証する。
            NodeTable table = new NodeTable(initialCapacity: 4, capacityLimit: 4);

            table.Add(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            table.Add(level: 2, lo: NodeTable.Bottom, hi: NodeTable.Top);

            Assert.Equal(2, table.Count);
            Assert.Equal(4, table.CapacityLimit);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => table.Add(level: 3, lo: NodeTable.Bottom, hi: NodeTable.Top));

            Assert.Contains("4", exception.Message, StringComparison.Ordinal);

            // 上限は「終端 2 個を含む ID の個数」なので、実ノード数の上限はそれより 2 小さい。
            Assert.Equal(table.CapacityLimit - NodeTable.FirstNodeId, table.Count);

            // 例外を投げた後も表は壊れていない（件数が進んでいない）。
            Assert.Equal(2, table.Count);
            Assert.Equal(4, table.NextId);
        }

        [Fact]
        public void DefaultCapacityLimitIsTheMaximumArrayLength()
        {
            NodeTable table = new NodeTable();

            Assert.Equal(NodeTable.MaxCapacity, table.CapacityLimit);
            Assert.Equal(Array.MaxLength, NodeTable.MaxCapacity);
        }

        [Fact]
        public void AddsOneMillionNodesAcrossManyResizes()
        {
            const int Count = 1_000_000;

            NodeTable table = new NodeTable(initialCapacity: 4);
            int resizes = 0;
            int capacity = table.Capacity;
            int previousId = NodeTable.Top;
            bool idsAreSequential = true;

            for (int i = 0; i < Count; i++)
            {
                // 直前に作ったノードを 0-枝に繋ぐことで、深さ 100 万本の鎖になる。
                int id = table.Add(level: i + 1, lo: previousId, hi: NodeTable.Top);

                if (id != NodeTable.FirstNodeId + i)
                {
                    idsAreSequential = false;
                }

                if (table.Capacity != capacity)
                {
                    capacity = table.Capacity;
                    resizes++;
                }

                previousId = id;
            }

            Assert.True(idsAreSequential);
            Assert.Equal(Count, table.Count);
            Assert.Equal(NodeTable.FirstNodeId + Count, table.NextId);

            // 4 から 100 万超まで倍化するので、リサイズは複数回（18 回）走っている。
            Assert.True(resizes > 1, $"expected multiple resizes, but observed {resizes}");
            Assert.True(table.Capacity >= NodeTable.FirstNodeId + Count);

            // 鎖の両端と中間を抜き取り確認する。
            Assert.Equal(NodeTable.Top, table[NodeTable.FirstNodeId].Lo);
            Assert.Equal(1, table[NodeTable.FirstNodeId].Level);

            int middle = NodeTable.FirstNodeId + (Count / 2);
            Assert.Equal(middle - 1, table[middle].Lo);
            Assert.Equal(NodeTable.Top, table[middle].Hi);

            int last = NodeTable.FirstNodeId + Count - 1;
            Assert.Equal(Count, table[last].Level);
            Assert.Equal(last - 1, table[last].Lo);
        }
    }
}

using System;
using System.Collections.Generic;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Internal;

namespace ZDD.Net.Tests.Core
{
    public class UniqueTableTests
    {
        [Fact]
        public void NewTableIsEmptyAndRoundsCapacityUpToAPowerOfTwo()
        {
            UniqueTable table = new UniqueTable(initialCapacity: 100);

            Assert.Equal(0, table.Count);
            Assert.Equal(128, table.Capacity);
            Assert.Equal(128 * 70 / 100, table.GrowThreshold);
            Assert.Equal(0, table.Nodes.Count);
        }

        [Fact]
        public void CapacityNeverDropsBelowTheMinimum()
        {
            UniqueTable table = new UniqueTable(initialCapacity: 1);

            Assert.Equal(UniqueTable.MinimumCapacity, table.Capacity);
        }

        [Fact]
        public void DefaultConstructorUsesTheDefaultCapacity()
        {
            UniqueTable table = new UniqueTable();

            Assert.Equal(UniqueTable.DefaultCapacity, table.Capacity);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ConstructorThrowsForNonPositiveCapacity(int initialCapacity)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => new UniqueTable(initialCapacity));

            Assert.Equal("initialCapacity", exception.ParamName);
        }

        [Fact]
        public void ConstructorThrowsWhenCapacityExceedsTheMaximum()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => new UniqueTable(UniqueTable.MaxCapacity + 1));

            Assert.Equal("initialCapacity", exception.ParamName);
        }

        [Fact]
        public void ConstructorThrowsForANullNodeTable()
        {
            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new UniqueTable(null!, UniqueTable.DefaultCapacity));

            Assert.Equal("nodes", exception.ParamName);
        }

        [Fact]
        public void ConstructorThrowsWhenTheNodeTableAlreadyHoldsNodes()
        {
            NodeTable nodes = new NodeTable();
            nodes.Add(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);

            // 既存ノードは一意化表に登録されていないので、そのまま被せると同形ノードが重複する。
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new UniqueTable(nodes, UniqueTable.DefaultCapacity));

            Assert.Equal("nodes", exception.ParamName);
        }

        [Fact]
        public void SameTripleAlwaysReturnsTheSameId()
        {
            UniqueTable table = new UniqueTable();

            int first = table.GetNode(level: 3, lo: NodeTable.Bottom, hi: NodeTable.Top);
            int second = table.GetNode(level: 3, lo: NodeTable.Bottom, hi: NodeTable.Top);
            int third = table.GetNode(level: 3, lo: NodeTable.Bottom, hi: NodeTable.Top);

            Assert.Equal(first, second);
            Assert.Equal(first, third);

            // 2 回目以降はノードを増やさない。
            Assert.Equal(1, table.Count);
            Assert.Equal(1, table.Nodes.Count);
        }

        [Fact]
        public void GetNodeStoresTheGivenTriple()
        {
            UniqueTable table = new UniqueTable();

            int child = table.GetNode(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            int id = table.GetNode(level: 4, lo: child, hi: NodeTable.Top);

            ref ZddNode node = ref table.Nodes[id];

            Assert.Equal(NodeTable.FirstNodeId, child);
            Assert.Equal(4, node.Level);
            Assert.Equal(child, node.Lo);
            Assert.Equal(NodeTable.Top, node.Hi);
        }

        [Fact]
        public void DifferentTriplesGetDifferentIds()
        {
            UniqueTable table = new UniqueTable();

            int a = table.GetNode(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            int b = table.GetNode(level: 1, lo: NodeTable.Top, hi: NodeTable.Top);
            int c = table.GetNode(level: 2, lo: NodeTable.Bottom, hi: NodeTable.Top);
            int d = table.GetNode(level: 2, lo: a, hi: b);

            Assert.Equal(4, new HashSet<int> { a, b, c, d }.Count);
            Assert.Equal(4, table.Count);
        }

        [Fact]
        public void ZeroSuppressedRuleReturnsLoWithoutCreatingANode()
        {
            UniqueTable table = new UniqueTable();

            int lo = table.GetNode(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            int countBefore = table.Count;

            // hi == ⊥ のノードは「その変数を含む組合せが無い」ことを意味し、lo と等しい。
            Assert.Equal(lo, table.GetNode(level: 5, lo: lo, hi: NodeTable.Bottom));
            Assert.Equal(NodeTable.Bottom, table.GetNode(level: 5, lo: NodeTable.Bottom, hi: NodeTable.Bottom));
            Assert.Equal(NodeTable.Top, table.GetNode(level: 5, lo: NodeTable.Top, hi: NodeTable.Bottom));

            Assert.Equal(countBefore, table.Count);
            Assert.Equal(countBefore, table.Nodes.Count);
        }

        [Fact]
        public void ZeroSuppressedRuleIsAppliedBeforeTheHashLookup()
        {
            UniqueTable table = new UniqueTable();

            // (level, lo, ⊥) は表に登録されないので、あとから同じ level/lo で hi を変えて
            // 引いても、削減された結果が居座っていることはない。
            int suppressed = table.GetNode(level: 2, lo: NodeTable.Top, hi: NodeTable.Bottom);
            int real = table.GetNode(level: 2, lo: NodeTable.Top, hi: NodeTable.Top);

            Assert.Equal(NodeTable.Top, suppressed);
            Assert.NotEqual(suppressed, real);
            Assert.Equal(1, table.Count);
        }

        [Fact]
        public void TryGetExistingFindsRegisteredNodesOnly()
        {
            UniqueTable table = new UniqueTable();
            int id = table.GetNode(level: 2, lo: NodeTable.Bottom, hi: NodeTable.Top);

            Assert.True(table.TryGetExisting(2, NodeTable.Bottom, NodeTable.Top, out int found));
            Assert.Equal(id, found);

            Assert.False(table.TryGetExisting(3, NodeTable.Bottom, NodeTable.Top, out int missing));
            Assert.Equal(NodeTable.Bottom, missing);

            // 探索だけなのでノードは増えない。
            Assert.Equal(1, table.Count);
        }

        [Fact]
        public void CollidingTriplesAreAllStoredAndRetrievedCorrectly()
        {
            const int Capacity = 1024;
            UniqueTable table = new UniqueTable(Capacity);

            // 同一スロットに落ちるキーを実際に探して使う（線形探索が正しく働かないと、
            // 取り違え・上書き・無限ループのいずれかで落ちる）。
            int[] levels = FindLevelsCollidingInSameSlot(Capacity, count: 32);
            Dictionary<int, int> idsByLevel = new Dictionary<int, int>();

            foreach (int level in levels)
            {
                idsByLevel[level] = table.GetNode(level, NodeTable.Bottom, NodeTable.Top);
            }

            Assert.Equal(Capacity, table.Capacity); // 倍化は起きていない（負荷率に届いていない）。
            Assert.Equal(levels.Length, table.Count);
            Assert.Equal(levels.Length, new HashSet<int>(idsByLevel.Values).Count);

            foreach (int level in levels)
            {
                Assert.Equal(idsByLevel[level], table.GetNode(level, NodeTable.Bottom, NodeTable.Top));

                ref ZddNode node = ref table.Nodes[idsByLevel[level]];
                Assert.Equal(level, node.Level);
                Assert.Equal(NodeTable.Bottom, node.Lo);
                Assert.Equal(NodeTable.Top, node.Hi);
            }
        }

        [Fact]
        public void CapacityDoublesExactlyWhenTheLoadFactorIsExceeded()
        {
            UniqueTable table = new UniqueTable(initialCapacity: UniqueTable.MinimumCapacity);

            Assert.Equal(4, table.Capacity);
            Assert.Equal(2, table.GrowThreshold);

            table.GetNode(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            table.GetNode(level: 2, lo: NodeTable.Bottom, hi: NodeTable.Top);

            Assert.Equal(4, table.Capacity);

            // 負荷率 70% を超える 3 個目でちょうど倍化する。
            table.GetNode(level: 3, lo: NodeTable.Bottom, hi: NodeTable.Top);

            Assert.Equal(8, table.Capacity);
            Assert.Equal(5, table.GrowThreshold);
            Assert.Equal(3, table.Count);
        }

        [Fact]
        public void ExistingLookupsNeverTriggerAResize()
        {
            UniqueTable table = new UniqueTable(initialCapacity: UniqueTable.MinimumCapacity);

            table.GetNode(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
            table.GetNode(level: 2, lo: NodeTable.Bottom, hi: NodeTable.Top);

            for (int i = 0; i < 100; i++)
            {
                table.GetNode(level: 1, lo: NodeTable.Bottom, hi: NodeTable.Top);
                table.GetNode(level: 5, lo: NodeTable.Top, hi: NodeTable.Bottom);
            }

            Assert.Equal(4, table.Capacity);
            Assert.Equal(2, table.Count);
        }

        [Fact]
        public void IdsObtainedBeforeARehashStillPointAtTheSameNodes()
        {
            UniqueTable table = new UniqueTable(initialCapacity: UniqueTable.MinimumCapacity);
            const int Before = 3;
            const int After = 500;

            int[] early = new int[Before];
            for (int level = 1; level <= Before; level++)
            {
                early[level - 1] = table.GetNode(level, NodeTable.Bottom, NodeTable.Top);
            }

            int capacityBefore = table.Capacity;

            for (int level = Before + 1; level <= Before + After; level++)
            {
                table.GetNode(level, NodeTable.Bottom, NodeTable.Top);
            }

            // 倍化が複数回走っていること。
            Assert.True(table.Capacity > capacityBefore);

            for (int level = 1; level <= Before; level++)
            {
                int id = early[level - 1];

                // ID は再ハッシュを跨いでも不変で、同じ内容を指し続ける。
                Assert.Equal(id, table.GetNode(level, NodeTable.Bottom, NodeTable.Top));
                Assert.True(table.TryGetExisting(level, NodeTable.Bottom, NodeTable.Top, out int found));
                Assert.Equal(id, found);

                ref ZddNode node = ref table.Nodes[id];
                Assert.Equal(level, node.Level);
                Assert.Equal(NodeTable.Bottom, node.Lo);
                Assert.Equal(NodeTable.Top, node.Hi);
            }

            Assert.Equal(Before + After, table.Count);
        }

        [Fact]
        public void ManyRegistrationsSurviveRepeatedDoubling()
        {
            const int Count = 100_000;
            UniqueTable table = new UniqueTable(initialCapacity: UniqueTable.MinimumCapacity);

            // 変数レベルを 1 段ずつ上げながら鎖状に積む（子の水準は常に親より下）。
            int[] chain = new int[Count + 1];
            chain[0] = NodeTable.Top;
            for (int level = 1; level <= Count; level++)
            {
                chain[level] = table.GetNode(level, chain[level - 1], NodeTable.Top);
            }

            Assert.Equal(Count, table.Count);
            Assert.Equal(Count, table.Nodes.Count);

            // 倍化が複数回走るだけの容量になっている（負荷率 70% を守っている）。
            Assert.True(table.Capacity >= 1 << 18, $"capacity was {table.Capacity}");
            Assert.True(table.Count <= table.GrowThreshold);

            // 全件を引き直しても 1 個も増えず、同じ ID が返る。
            for (int level = 1; level <= Count; level++)
            {
                Assert.Equal(chain[level], table.GetNode(level, chain[level - 1], NodeTable.Top));
            }

            Assert.Equal(Count, table.Count);
            Assert.Equal(Count, new HashSet<int>(chain[1..]).Count);
        }

        /// <summary>
        /// サイズ <paramref name="tableSize"/> の表で同一スロットに落ちる
        /// <c>(level, ⊥, ⊤)</c> のレベルを <paramref name="count"/> 個見つける。
        /// </summary>
        private static int[] FindLevelsCollidingInSameSlot(int tableSize, int count)
        {
            Dictionary<int, List<int>> levelsBySlot = new Dictionary<int, List<int>>();

            for (int level = 1; level < 1_000_000; level++)
            {
                int slot = Hashing.IndexFor(Hashing.Combine(level, NodeTable.Bottom, NodeTable.Top), tableSize);

                if (!levelsBySlot.TryGetValue(slot, out List<int>? levels))
                {
                    levels = new List<int>();
                    levelsBySlot.Add(slot, levels);
                }

                levels.Add(level);
                if (levels.Count == count)
                {
                    return levels.ToArray();
                }
            }

            throw new InvalidOperationException(
                $"Could not find {count} levels hashing to the same slot of a table of size {tableSize}.");
        }
    }
}

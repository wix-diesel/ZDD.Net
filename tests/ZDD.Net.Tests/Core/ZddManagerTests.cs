using System;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    public class ZddManagerTests
    {
        [Fact]
        public void NewManagerHasNoNodes()
        {
            using ZddManager manager = new ZddManager(4);

            Assert.Equal(4, manager.VariableCount);
            Assert.Equal(0L, manager.NodeCount);
            Assert.False(manager.IsDisposed);
        }

        [Fact]
        public void ConstructorRejectsANegativeVariableCount()
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => new ZddManager(-1));

            Assert.Equal("variableCount", exception.ParamName);
        }

        [Fact]
        public void AManagerWithNoVariablesStillHasTheTerminals()
        {
            using ZddManager manager = new ZddManager(0);

            Assert.True(manager.Empty.IsEmpty);
            Assert.True(manager.Base.IsBase);
            Assert.Equal(0L, manager.NodeCount);
        }

        // ---- 終端 ----

        [Fact]
        public void EmptyIsTheBottomTerminalAndBaseIsTheTopTerminal()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd empty = manager.Empty;
            Zdd @base = manager.Base;

            Assert.True(empty.IsEmpty);
            Assert.False(empty.IsBase);
            Assert.True(@base.IsBase);
            Assert.False(@base.IsEmpty);
            Assert.NotEqual(empty, @base);

            // 終端はノード表を消費しない。
            Assert.Equal(0L, manager.NodeCount);
            Assert.Equal(0L, empty.NodeCount);
            Assert.Equal(0L, @base.NodeCount);
        }

        [Fact]
        public void TheTerminalsAreStableAcrossCalls()
        {
            using ZddManager manager = new ZddManager(3);

            Assert.Equal(manager.Empty, manager.Empty);
            Assert.Equal(manager.Base, manager.Base);
        }

        // ---- Singleton ----

        [Fact]
        public void SingletonIsNeitherTerminalAndUsesExactlyOneNode()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd single = manager.Singleton(1);

            Assert.False(single.IsEmpty);
            Assert.False(single.IsBase);
            Assert.Equal(1L, single.NodeCount);
            Assert.Equal(1L, manager.NodeCount);
            Assert.Equal(new[] { 1 }, single.Support());
        }

        [Fact]
        public void SingletonsForDifferentItemsAreDifferentFamilies()
        {
            using ZddManager manager = new ZddManager(3);

            Assert.NotEqual(manager.Singleton(0), manager.Singleton(1));
            Assert.Equal(2L, manager.NodeCount);
        }

        [Fact]
        public void SingletonIsSharedWhenAskedForTwice()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd first = manager.Singleton(2);
            Zdd second = manager.Singleton(2);

            Assert.Equal(first, second);
            Assert.Equal(1L, manager.NodeCount);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(3)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void SingletonRejectsAnItemOutsideTheVariableRange(int item)
        {
            using ZddManager manager = new ZddManager(3);

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => manager.Singleton(item));

            Assert.Equal("item", exception.ParamName);
        }

        [Fact]
        public void SingletonAlwaysThrowsWhenTheManagerHasNoVariables()
        {
            using ZddManager manager = new ZddManager(0);

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Singleton(0));
        }

        // ---- item ↔ level ----

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(64)]
        public void ItemAndLevelRoundTripAcrossTheWholeRange(int variableCount)
        {
            using ZddManager manager = new ZddManager(variableCount);

            for (int item = 0; item < variableCount; item++)
            {
                int level = manager.LevelOf(item);

                Assert.InRange(level, 1, variableCount);
                Assert.Equal(item, manager.ItemOf(level));
            }

            // 境界: item 0 が最上位（根側）、item N-1 が最下位（葉側）。
            Assert.Equal(variableCount, manager.LevelOf(0));
            Assert.Equal(1, manager.LevelOf(variableCount - 1));
            Assert.Equal(0, manager.ItemOf(variableCount));
            Assert.Equal(variableCount - 1, manager.ItemOf(1));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        public void LevelOfRejectsAnItemOutsideTheVariableRange(int item)
        {
            using ZddManager manager = new ZddManager(4);

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => manager.LevelOf(item));

            Assert.Equal("item", exception.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(5)]
        public void ItemOfRejectsALevelOutsideTheVariableRange(int level)
        {
            using ZddManager manager = new ZddManager(4);

            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => manager.ItemOf(level));

            Assert.Equal("level", exception.ParamName);
        }

        // ---- 手で組み立てた族 ----

        [Fact]
        public void ItemsAndTheirLevelsAgreeWithTheNodesThatAreCreated()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd single = manager.Singleton(0);

            Assert.Equal(manager.LevelOf(0), manager.Table.Nodes[single.Id].Level);
        }

        [Fact]
        public void SingletonsCanBeCombinedByHandIntoASmallFamily()
        {
            using ZddManager manager = new ZddManager(2);

            // {{0}, {1}}: item 0 を含む集合は {0}（残りは空集合 = ⊤）、含まない集合は {1}。
            Zdd family = manager.CreateNode(0, lo: manager.Singleton(1), hi: manager.Base);

            Assert.False(family.IsEmpty);
            Assert.False(family.IsBase);
            Assert.Equal(2L, family.NodeCount);
            Assert.Equal(new[] { 0, 1 }, family.Support());
        }

        [Fact]
        public void TheSameFamilyBuiltTwoDifferentWaysGetsTheSameNodeId()
        {
            using ZddManager manager = new ZddManager(2);

            // 経路 A: Singleton をそのまま 0-枝に置く。
            Zdd viaSingleton = manager.CreateNode(0, lo: manager.Singleton(1), hi: manager.Base);

            // 経路 B: {{1}} をノードから組み立ててから 0-枝に置く。
            Zdd rebuiltChild = manager.CreateNode(1, lo: manager.Empty, hi: manager.Base);
            Zdd viaHandBuiltChild = manager.CreateNode(0, lo: rebuiltChild, hi: manager.Base);

            Assert.Equal(manager.Singleton(1), rebuiltChild);
            Assert.Equal(viaSingleton, viaHandBuiltChild);

            // 一意化が効いていれば、2 つの経路を通ってもノードは 2 個しか増えない。
            Assert.Equal(2L, manager.NodeCount);
        }

        [Fact]
        public void ANodeWhoseOneEdgeIsEmptyIsSuppressed()
        {
            using ZddManager manager = new ZddManager(2);

            Zdd child = manager.Singleton(1);
            long before = manager.NodeCount;

            // ゼロサプレス削減規則: 1-枝が ∅ を指すノードは作られず、0-枝がそのまま返る。
            Zdd suppressed = manager.CreateNode(0, lo: child, hi: manager.Empty);

            Assert.Equal(child, suppressed);
            Assert.Equal(before, manager.NodeCount);
        }

        [Fact]
        public void CreateNodeRejectsAChildThatBranchesOnAnEarlierItem()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd rootSide = manager.Singleton(0);

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => manager.CreateNode(1, lo: manager.Empty, hi: rootSide));

            Assert.Equal("hi", exception.ParamName);
        }

        [Fact]
        public void CreateNodeRejectsAChildFromAnotherManager()
        {
            using ZddManager manager = new ZddManager(3);
            using ZddManager other = new ZddManager(3);

            Zdd foreign = other.Singleton(1);

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => manager.CreateNode(0, lo: foreign, hi: manager.Base));

            Assert.Equal("lo", exception.ParamName);
        }

        [Fact]
        public void CreateNodeRejectsADefaultHandle()
        {
            using ZddManager manager = new ZddManager(3);

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => manager.CreateNode(0, lo: manager.Base, hi: default));

            Assert.Equal("hi", exception.ParamName);
        }

        // ---- NodeCount / Support ----

        [Fact]
        public void SupportOnlyReportsTheItemsThatAreActuallyUsed()
        {
            using ZddManager manager = new ZddManager(5);

            Zdd family = manager.Singleton(3);

            Assert.Equal(new[] { 3 }, family.Support());
            Assert.Empty(manager.Empty.Support());
            Assert.Empty(manager.Base.Support());
        }

        [Fact]
        public void SupportAndNodeCountCountSharedNodesOnlyOnce()
        {
            using ZddManager manager = new ZddManager(3);

            // {{2}} を 0-枝と 1-枝の両方から指す族。到達ノードは根と {{2}} の 2 個。
            Zdd shared = manager.Singleton(2);
            Zdd family = manager.CreateNode(0, lo: shared, hi: shared);

            Assert.Equal(2L, family.NodeCount);
            Assert.Equal(new[] { 0, 2 }, family.Support());
        }

        [Fact]
        public void NodeCountAndSupportDoNotRecurseOnADeepDiagram()
        {
            // 変数 10 万本の 1 本鎖（族 {{0,1,...,99999}}）。再帰実装なら
            // StackOverflowException でプロセスごと落ちる（docs/PLAN.md §4.5）。
            const int VariableCount = 100_000;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd family = manager.Base;
            for (int item = VariableCount - 1; item >= 0; item--)
            {
                family = manager.CreateNode(item, lo: manager.Empty, hi: family);
            }

            Assert.Equal((long)VariableCount, family.NodeCount);
            Assert.Equal(VariableCount, family.Support().Length);
            Assert.Equal(0, family.Support()[0]);
            Assert.Equal(VariableCount - 1, family.Support()[VariableCount - 1]);
        }

        // ---- オプション ----

        [Fact]
        public void OptionsAreReadAtConstructionTime()
        {
            ZddManagerOptions options = new ZddManagerOptions
            {
                InitialNodeCapacity = 8,
                InitialUniqueTableCapacity = 8,
            };

            using ZddManager manager = new ZddManager(4, options);

            Assert.Equal(8 + 2, manager.Table.Nodes.Capacity);
            Assert.Equal(8, manager.Table.Capacity);

            // 生成後にオプションを変えても、既にできたマネージャには影響しない。
            options.InitialNodeCapacity = 4096;
            Assert.Equal(8 + 2, manager.Table.Nodes.Capacity);
        }

        [Fact]
        public void TheDefaultOptionsAreUsedWhenNoneIsGiven()
        {
            using ZddManager manager = new ZddManager(4);

            Assert.Equal(ZddManagerOptions.DefaultInitialNodeCapacity + 2, manager.Table.Nodes.Capacity);
            Assert.Equal(ZddManagerOptions.DefaultInitialUniqueTableCapacity, manager.Table.Capacity);
        }

        [Fact]
        public void ASmallInitialCapacityStillGrowsToHoldEveryNode()
        {
            ZddManagerOptions options = new ZddManagerOptions
            {
                InitialNodeCapacity = 1,
                InitialUniqueTableCapacity = 1,
            };

            using ZddManager manager = new ZddManager(64, options);

            for (int item = 0; item < 64; item++)
            {
                Assert.Equal(new[] { item }, manager.Singleton(item).Support());
            }

            Assert.Equal(64L, manager.NodeCount);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void OptionsRejectNonPositiveCapacities(int capacity)
        {
            ZddManagerOptions options = new ZddManagerOptions();

            Assert.Throws<ArgumentOutOfRangeException>(() => options.InitialNodeCapacity = capacity);
            Assert.Throws<ArgumentOutOfRangeException>(() => options.InitialUniqueTableCapacity = capacity);
        }

        [Fact]
        public void OptionsRejectCapacitiesBeyondWhatTheTablesCanHold()
        {
            ZddManagerOptions options = new ZddManagerOptions();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => options.InitialNodeCapacity = ZddManagerOptions.MaxInitialNodeCapacity + 1);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => options.InitialUniqueTableCapacity = UniqueTable.MaxCapacity + 1);
        }

        // ---- 破棄 ----

        [Fact]
        public void DisposingReleasesTheTablesAndIsIdempotent()
        {
            ZddManager manager = new ZddManager(3);

            manager.Dispose();
            manager.Dispose();

            Assert.True(manager.IsDisposed);
            Assert.Equal(3, manager.VariableCount);
        }

        [Fact]
        public void EveryOperationOnADisposedManagerThrows()
        {
            ZddManager manager = new ZddManager(3);
            Zdd single = manager.Singleton(1);

            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => manager.Empty);
            Assert.Throws<ObjectDisposedException>(() => manager.Base);
            Assert.Throws<ObjectDisposedException>(() => manager.NodeCount);
            Assert.Throws<ObjectDisposedException>(() => manager.Singleton(0));
            Assert.Throws<ObjectDisposedException>(() => single.NodeCount);
            Assert.Throws<ObjectDisposedException>(() => single.Support());
        }

        [Fact]
        public void AHandleFromADisposedManagerStillCompares()
        {
            ZddManager manager = new ZddManager(3);
            Zdd single = manager.Singleton(1);
            Zdd same = manager.Singleton(1);

            manager.Dispose();

            // 等値比較はノード表を見ないので、破棄後も使える。
            Assert.Equal(single, same);
            Assert.True(single.IsDefault == false);
            Assert.False(single.IsEmpty);
        }
    }
}

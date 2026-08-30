using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="Zdd.Change"/> / <see cref="Zdd.OnSet"/> / <see cref="Zdd.OffSet"/> の検証。
    /// </summary>
    /// <remarks>
    /// 照合相手はこのファイル内の素朴実装（集合をビットマスクで表した <see cref="SortedSet{T}"/>）。
    /// M1-6 で共通の総当たり照合基盤が入ったら、そちらへ寄せる。
    /// </remarks>
    public class UnaryOperationTests
    {
        // ---- 総当たり照合 ----

        [Fact]
        public void EveryFamilyOfThreeVariablesMatchesTheNaiveImplementation()
        {
            const int VariableCount = 3;
            int maskCount = 1 << VariableCount;

            using ZddManager manager = new ZddManager(VariableCount);

            // 3 変数の集合は 8 個。その部分集合＝族は 2^8 = 256 通りで、すべて試せる。
            for (int family = 0; family < 1 << 8; family++)
            {
                SortedSet<int> masks = new SortedSet<int>(
                    Enumerable.Range(0, maskCount).Where(mask => (family & (1 << mask)) != 0));

                Zdd zdd = Build(manager, masks);
                Assert.True(masks.SetEquals(ToMasks(manager, zdd)), "The family builder itself must round-trip.");

                for (int item = 0; item < VariableCount; item++)
                {
                    AssertUnaryOperationsMatchNaive(manager, zdd, masks, item);
                }
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(10)]
        [InlineData(12)]
        public void RandomFamiliesMatchTheNaiveImplementation(int variableCount)
        {
            Random random = new Random(20260830 + variableCount);
            int maskCount = 1 << variableCount;

            using ZddManager manager = new ZddManager(variableCount);

            for (int round = 0; round < 50; round++)
            {
                SortedSet<int> masks = RandomFamily(random, maskCount);
                Zdd zdd = Build(manager, masks);
                Assert.True(masks.SetEquals(ToMasks(manager, zdd)), "The family builder itself must round-trip.");

                for (int item = 0; item < variableCount; item++)
                {
                    AssertUnaryOperationsMatchNaive(manager, zdd, masks, item);
                }
            }
        }

        // ---- 代数的な性質 ----

        [Fact]
        public void ChangeAppliedTwiceIsTheIdentity()
        {
            const int VariableCount = 8;
            Random random = new Random(4649);

            using ZddManager manager = new ZddManager(VariableCount);

            for (int round = 0; round < 50; round++)
            {
                Zdd zdd = Build(manager, RandomFamily(random, 1 << VariableCount));

                for (int item = 0; item < VariableCount; item++)
                {
                    Assert.Equal(zdd, zdd.Change(item).Change(item));
                }
            }
        }

        [Fact]
        public void OnSetAndOffSetSplitTheFamilyInTwo()
        {
            const int VariableCount = 8;
            Random random = new Random(1729);

            using ZddManager manager = new ZddManager(VariableCount);

            for (int round = 0; round < 50; round++)
            {
                SortedSet<int> masks = RandomFamily(random, 1 << VariableCount);
                Zdd zdd = Build(manager, masks);

                for (int item = 0; item < VariableCount; item++)
                {
                    // 和は演算としてはまだ無い（M1-7）ので、素朴表現の側で合わせる。
                    SortedSet<int> without = ToMasks(manager, zdd.OffSet(item));
                    SortedSet<int> with = ToMasks(manager, zdd.OnSet(item).Change(item));

                    Assert.Empty(without.Intersect(with));
                    Assert.True(masks.SetEquals(without.Union(with)));
                }
            }
        }

        [Fact]
        public void OnSetAndOffSetOfTheTerminalsAreTheExpectedTerminals()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd empty = manager.Empty;
            Zdd @base = manager.Base;

            Assert.Equal(empty, empty.Change(2));
            Assert.Equal(empty, empty.OnSet(2));
            Assert.Equal(empty, empty.OffSet(2));

            // {∅} の item を反転すると {{item}}、すなわち Singleton になる。
            Assert.Equal(manager.Singleton(2), @base.Change(2));
            Assert.Equal(empty, @base.OnSet(2));
            Assert.Equal(@base, @base.OffSet(2));

            Assert.Equal(@base, manager.Singleton(2).OnSet(2));
            Assert.Equal(empty, manager.Singleton(2).OffSet(2));
        }

        [Fact]
        public void TheAliasesAreTheSameOperations()
        {
            using ZddManager manager = new ZddManager(6);

            Zdd zdd = Build(manager, new SortedSet<int> { 0b000001, 0b010010, 0b010011, 0b101000 });

            for (int item = 0; item < 6; item++)
            {
                Assert.Equal(zdd.OnSet(item), zdd.Subset1(item));
                Assert.Equal(zdd.OffSet(item), zdd.Subset0(item));
            }
        }

        // ---- キャッシュ ----

        [Fact]
        public void TheResultIsTheSameWithAndWithoutTheOperationCache()
        {
            const int VariableCount = 10;
            Random random = new Random(31337);

            ZddManagerOptions disabled = new ZddManagerOptions { InitialCacheCapacity = 0, MaxCacheCapacity = 0 };

            using ZddManager cached = new ZddManager(VariableCount);
            using ZddManager uncached = new ZddManager(VariableCount, disabled);

            for (int round = 0; round < 30; round++)
            {
                SortedSet<int> masks = RandomFamily(random, 1 << VariableCount);

                Zdd withCache = Build(cached, masks);
                Zdd withoutCache = Build(uncached, masks);

                for (int item = 0; item < VariableCount; item++)
                {
                    Assert.True(ToMasks(cached, withCache.Change(item))
                        .SetEquals(ToMasks(uncached, withoutCache.Change(item))));
                    Assert.True(ToMasks(cached, withCache.OnSet(item))
                        .SetEquals(ToMasks(uncached, withoutCache.OnSet(item))));
                    Assert.True(ToMasks(cached, withCache.OffSet(item))
                        .SetEquals(ToMasks(uncached, withoutCache.OffSet(item))));
                }
            }
        }

        [Fact]
        public void SharedNodesAreVisitedOnceEvenWithoutTheOperationCache()
        {
            // 各変数が「入っていても入っていなくてもよい」鎖（＝冪集合）。パスは 2^64 本あるが、
            // ノードは 64 個しかない。途中結果表が効いていなければ、この呼び出しは終わらない。
            const int VariableCount = 64;

            ZddManagerOptions disabled = new ZddManagerOptions { InitialCacheCapacity = 0, MaxCacheCapacity = 0 };
            using ZddManager manager = new ZddManager(VariableCount, disabled);

            Zdd powerSet = manager.Base;
            for (int item = VariableCount - 1; item >= 0; item--)
            {
                powerSet = manager.CreateNode(item, powerSet, powerSet);
            }

            Assert.Equal((long)VariableCount, powerSet.NodeCount);

            // 冪集合は「item を含む／含まない」で対称なので、反転しても変わらない。
            Assert.Equal(powerSet, powerSet.Change(VariableCount - 1));

            // item を含む集合から item を除いたものは、残りの変数の冪集合（ノードが 1 つ減る）。
            Assert.Equal((long)(VariableCount - 1), powerSet.OnSet(VariableCount - 1).NodeCount);
            Assert.Equal((long)(VariableCount - 1), powerSet.OffSet(VariableCount - 1).NodeCount);
        }

        // ---- 深い ZDD（スタックオーバーフロー回帰テスト） ----

        [Fact]
        public void DeepDiagramsDoNotOverflowTheStack()
        {
            // 変数 10 万の鎖。素直な再帰実装ならここで StackOverflowException になり、
            // .NET では catch できずプロセスごと落ちる（docs/PLAN.md §4.5）。
            const int VariableCount = 100_000;
            int deepest = VariableCount - 1;

            using ZddManager manager = new ZddManager(VariableCount);

            // { {i} : 0 <= i < VariableCount }。深さ・ノード数ともに VariableCount。
            Zdd singletons = manager.Empty;
            for (int item = deepest; item >= 0; item--)
            {
                singletons = manager.CreateNode(item, singletons, manager.Base);
            }

            Assert.Equal((long)VariableCount, singletons.NodeCount);

            // 最も深い item を対象にすると、根から葉まで全段を降りることになる。
            // Change: {deepest} は ∅ に、他の {i} は {i, deepest} になる。
            // 出来上がるのは「item ごとの節 (deepest 個) ＋ 共有される {{deepest}} の節 1 個」。
            Zdd changed = singletons.Change(deepest);
            Assert.Equal((long)VariableCount, changed.NodeCount);

            // OnSet: deepest を含む集合は {deepest} だけ。そこから deepest を除くと ∅。
            Assert.True(singletons.OnSet(deepest).IsBase);

            // OffSet: {deepest} だけが消える。
            Assert.Equal((long)(VariableCount - 1), singletons.OffSet(deepest).NodeCount);

            // 反転を 2 回かけても元に戻る（深いまま）。
            Assert.Equal(singletons, changed.Change(deepest));
        }

        // ---- 引数の検査 ----

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        [InlineData(int.MaxValue)]
        public void AnItemOutsideTheManagerIsRejected(int item)
        {
            using ZddManager manager = new ZddManager(4);

            Zdd zdd = manager.Singleton(0);

            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.Change(item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.OnSet(item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.OffSet(item)).ParamName);
        }

        [Fact]
        public void AFamilyFromAnotherManagerIsRejected()
        {
            using ZddManager one = new ZddManager(4);
            using ZddManager other = new ZddManager(4);

            Zdd foreign = other.Singleton(0);

            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.Change(foreign, 1)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.OnSet(foreign, 1)).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.OffSet(foreign, 1)).ParamName);
        }

        [Fact]
        public void ADefaultHandleHasNoOperations()
        {
            Zdd none = default;

            Assert.Throws<InvalidOperationException>(() => none.Change(0));
            Assert.Throws<InvalidOperationException>(() => none.OnSet(0));
            Assert.Throws<InvalidOperationException>(() => none.OffSet(0));
        }

        [Fact]
        public void OperationsOnADisposedManagerThrow()
        {
            ZddManager manager = new ZddManager(4);
            Zdd zdd = manager.Singleton(1);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => zdd.Change(1));
            Assert.Throws<ObjectDisposedException>(() => zdd.OnSet(1));
            Assert.Throws<ObjectDisposedException>(() => zdd.OffSet(1));
        }

        // ---- 素朴実装（M1-6 の共通基盤が入るまでの簡易版） ----

        private static void AssertUnaryOperationsMatchNaive(
            ZddManager manager,
            in Zdd zdd,
            SortedSet<int> masks,
            int item)
        {
            int bit = 1 << item;

            SortedSet<int> change = new SortedSet<int>(masks.Select(mask => mask ^ bit));
            SortedSet<int> onSet = new SortedSet<int>(masks.Where(mask => (mask & bit) != 0).Select(mask => mask & ~bit));
            SortedSet<int> offSet = new SortedSet<int>(masks.Where(mask => (mask & bit) == 0));

            AssertSameFamily(manager, change, zdd.Change(item));
            AssertSameFamily(manager, onSet, zdd.OnSet(item));
            AssertSameFamily(manager, offSet, zdd.OffSet(item));
        }

        private static void AssertSameFamily(ZddManager manager, SortedSet<int> expected, in Zdd actual)
        {
            SortedSet<int> produced = ToMasks(manager, actual);

            Assert.True(
                expected.SetEquals(produced),
                $"expected {{{string.Join(", ", expected)}}} but the diagram holds {{{string.Join(", ", produced)}}}.");

            // ZDD は正準形なので、同じ族なら同じハンドルになっていなければならない。
            Assert.Equal(Build(manager, expected), actual);
        }

        private static SortedSet<int> RandomFamily(Random random, int maskCount)
        {
            SortedSet<int> masks = new SortedSet<int>();
            int size = random.Next(0, Math.Min(maskCount, 24) + 1);

            for (int i = 0; i < size; i++)
            {
                masks.Add(random.Next(maskCount));
            }

            return masks;
        }

        /// <summary>
        /// 集合をビットマスクで表した族から ZDD を組み立てる。
        /// item ごとに族を「その item を含まない側／含む側」に割り、下の段から
        /// <see cref="ZddManager.CreateNode"/> で積み上げる。<b>再帰しない</b>。
        /// </summary>
        private static Zdd Build(ZddManager manager, IEnumerable<int> masks)
        {
            int variableCount = manager.VariableCount;

            List<Dictionary<string, Group>> levels = new List<Dictionary<string, Group>>();
            for (int item = 0; item <= variableCount; item++)
            {
                levels.Add(new Dictionary<string, Group>(StringComparer.Ordinal));
            }

            string rootKey = Register(levels[0], masks);

            for (int item = 0; item < variableCount; item++)
            {
                int bit = 1 << item;

                foreach (Group group in levels[item].Values.ToList())
                {
                    group.LoKey = Register(
                        levels[item + 1],
                        group.Masks.Where(mask => (mask & bit) == 0));
                    group.HiKey = Register(
                        levels[item + 1],
                        group.Masks.Where(mask => (mask & bit) != 0).Select(mask => mask & ~bit));
                }
            }

            // 族はマスクの集合だけで決まるので、段をまたいで同じキーが現れても同じノードでよい。
            Dictionary<string, Zdd> built = new Dictionary<string, Zdd>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Group> entry in levels[variableCount])
            {
                // 全 item を割り振り終えた段に残るのは空集合だけ。
                built[entry.Key] = entry.Value.Masks.Count == 0 ? manager.Empty : manager.Base;
            }

            for (int item = variableCount - 1; item >= 0; item--)
            {
                foreach (KeyValuePair<string, Group> entry in levels[item])
                {
                    Group group = entry.Value;
                    built[entry.Key] = manager.CreateNode(item, built[group.LoKey!], built[group.HiKey!]);
                }
            }

            return built[rootKey];
        }

        private static string Register(Dictionary<string, Group> level, IEnumerable<int> masks)
        {
            SortedSet<int> sorted = new SortedSet<int>(masks);
            string key = string.Join(",", sorted);

            if (!level.ContainsKey(key))
            {
                level.Add(key, new Group(sorted));
            }

            return key;
        }

        /// <summary>
        /// ZDD が表す族を、集合のビットマスクの集合として取り出す。
        /// 明示スタックで根から終端までのパスを全部辿る（変数 12 個までなので高々 4096 本）。
        /// </summary>
        private static SortedSet<int> ToMasks(ZddManager manager, in Zdd zdd)
        {
            SortedSet<int> masks = new SortedSet<int>();
            NodeTable nodes = manager.Table.Nodes;

            Stack<(int Id, int Mask)> stack = new Stack<(int, int)>();
            stack.Push((zdd.Id, 0));

            while (stack.Count > 0)
            {
                (int id, int mask) = stack.Pop();

                if (id == NodeTable.Bottom)
                {
                    continue;
                }

                if (id == NodeTable.Top)
                {
                    masks.Add(mask);
                    continue;
                }

                ZddNode node = nodes[id];
                int item = manager.ItemOf(node.Level);

                stack.Push((node.Lo, mask));
                stack.Push((node.Hi, mask | (1 << item)));
            }

            return masks;
        }

        /// <summary>組み立ての途中で現れる「ある段より下だけを見た族」。</summary>
        private sealed class Group
        {
            public Group(SortedSet<int> masks) => Masks = masks;

            public SortedSet<int> Masks { get; }

            public string? LoKey { get; set; }

            public string? HiKey { get; set; }
        }
    }
}

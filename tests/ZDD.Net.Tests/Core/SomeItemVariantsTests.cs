using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// <see cref="Zdd.RemoveSomeItem()"/> / <see cref="Zdd.AddSomeItem()"/> /
    /// <see cref="Zdd.RemoveAddSomeItems()"/> (M6-7, issue #142).
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="BruteForceFamily"/>（定義をそのままループで書いた素朴実装）で、
    /// 比較は <see cref="FamilyAssert.AssertSameFamily(string?, in Zdd, BruteForceFamily, BruteForceFamily?)"/>
    /// が行う。
    /// </remarks>
    public class SomeItemVariantsTests
    {
        // ---- 総当たり照合 ----

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(8)]
        [InlineData(FamilyCases.DefaultVariableCount)]
        [InlineData(FamilyCases.ExhaustiveVariableLimit)]
        public void RandomFamiliesMatchTheNaiveImplementation(int variableCount)
        {
            using ZddManager manager = new ZddManager(variableCount);

            foreach (BruteForceFamily family in
                FamilyCases.RandomFamilies(variableCount, 8, seed: 9142000 + variableCount))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                foreach (int[] items in ItemSubsetCases(variableCount, seed: 9142001 + variableCount))
                {
                    string label = $"items=[{string.Join(", ", items)}]";

                    FamilyAssert.AssertSameFamily(
                        $"f.RemoveSomeItem({label})",
                        zdd.RemoveSomeItem(items),
                        family.RemoveSomeItem(items),
                        family);

                    FamilyAssert.AssertSameFamily(
                        $"f.AddSomeItem({label})",
                        zdd.AddSomeItem(items),
                        family.AddSomeItem(items),
                        family);

                    FamilyAssert.AssertSameFamily(
                        $"f.RemoveAddSomeItems({label})",
                        zdd.RemoveAddSomeItems(items),
                        family.RemoveAddSomeItems(items),
                        family);
                }
            }
        }

        [Fact]
        public void EverySingleSetFamilyMatchesTheNaiveImplementation()
        {
            const int VariableCount = 7;

            using ZddManager manager = new ZddManager(VariableCount);
            int[] allItems = Enumerable.Range(0, VariableCount).ToArray();

            foreach (int mask in FamilyCases.AllSubsets(VariableCount))
            {
                BruteForceFamily family = BruteForceFamily.FromMasks(VariableCount, [mask]);
                Zdd zdd = ZddFamilies.Build(manager, family);

                FamilyAssert.AssertSameFamily("f.RemoveSomeItem()", zdd.RemoveSomeItem(allItems), family.RemoveSomeItem(allItems), family);
                FamilyAssert.AssertSameFamily("f.AddSomeItem()", zdd.AddSomeItem(allItems), family.AddSomeItem(allItems), family);
                FamilyAssert.AssertSameFamily("f.RemoveAddSomeItems()", zdd.RemoveAddSomeItems(allItems), family.RemoveAddSomeItems(allItems), family);
            }
        }

        // ---- 引数なし版 == 全変数を渡した items 版 ----

        [Fact]
        public void TheParameterlessOverloadsUseEveryVariableOfTheManager()
        {
            const int VariableCount = 6;

            using ZddManager manager = new ZddManager(VariableCount);
            int[] everyItem = Enumerable.Range(0, VariableCount).ToArray();

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 20, seed: 9142100))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                Assert.Equal(zdd.RemoveSomeItem(everyItem), zdd.RemoveSomeItem());
                Assert.Equal(zdd.AddSomeItem(everyItem), zdd.AddSomeItem());
                Assert.Equal(zdd.RemoveAddSomeItems(everyItem), zdd.RemoveAddSomeItems());
            }
        }

        // ---- 境界 ----

        [Fact]
        public void RemoveSomeItemOfTheBoundaryFamiliesIsTheExpectedOne()
        {
            using ZddManager manager = new ZddManager(4);

            // ∅ には除く要素がある集合が無い。
            Assert.Equal(manager.Empty, manager.Empty.RemoveSomeItem());

            // {∅} の唯一の要素は空集合で、除ける item が無い。
            Assert.Equal(manager.Empty, manager.Base.RemoveSomeItem());

            // {0} から要素を 1 つ除くと {∅} になる。
            Assert.Equal(manager.Base, manager.Singleton(0).RemoveSomeItem());

            // items を渡さなければ、どの族でも空になる（和を取る対象が無い）。
            Assert.Equal(manager.Empty, manager.Singleton(0).RemoveSomeItem([]));
        }

        [Fact]
        public void AddSomeItemOfTheBoundaryFamiliesIsTheExpectedOne()
        {
            using ZddManager manager = new ZddManager(4);

            // ∅ には要素を足す集合が無い。
            Assert.Equal(manager.Empty, manager.Empty.AddSomeItem());

            // {∅} に 1 要素足すと、全 item の単集合の族になる。
            Zdd singletons = manager.Singleton(0) | manager.Singleton(1) | manager.Singleton(2) | manager.Singleton(3);
            Assert.Equal(singletons, manager.Base.AddSomeItem());

            // items を渡さなければ、どの族でも空になる。
            Assert.Equal(manager.Empty, manager.Base.AddSomeItem([]));
        }

        [Fact]
        public void RemoveAddSomeItemsOfTheBoundaryFamiliesIsTheExpectedOne()
        {
            using ZddManager manager = new ZddManager(4);

            // ∅ には手を加える集合が無い。
            Assert.Equal(manager.Empty, manager.Empty.RemoveAddSomeItems());

            // {∅} には除ける要素が無い。
            Assert.Equal(manager.Empty, manager.Base.RemoveAddSomeItems());

            // items が 1 個以下では e ≠ e' の組が作れない。
            Assert.Equal(manager.Empty, manager.Singleton(0).RemoveAddSomeItems([]));
            Assert.Equal(manager.Empty, manager.Singleton(0).RemoveAddSomeItems(0));

            // {0} から 0 を除き 1 を足すと {1} になる。
            Assert.Equal(manager.Singleton(1), manager.Singleton(0).RemoveAddSomeItems(0, 1));
        }

        // ---- 引数の検査 ----

        [Fact]
        public void ADefaultHandleHasNoOperations()
        {
            Zdd none = default;

            Assert.Throws<InvalidOperationException>(() => none.RemoveSomeItem());
            Assert.Throws<InvalidOperationException>(() => none.RemoveSomeItem(0));
            Assert.Throws<InvalidOperationException>(() => none.AddSomeItem());
            Assert.Throws<InvalidOperationException>(() => none.AddSomeItem(0));
            Assert.Throws<InvalidOperationException>(() => none.RemoveAddSomeItems());
            Assert.Throws<InvalidOperationException>(() => none.RemoveAddSomeItems(0));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(4)]
        [InlineData(int.MaxValue)]
        public void AnItemOutsideTheManagerIsRejected(int item)
        {
            using ZddManager manager = new ZddManager(4);
            Zdd zdd = manager.Singleton(0);

            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.RemoveSomeItem(item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.RemoveSomeItem(0, item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.AddSomeItem(item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.AddSomeItem(0, item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.RemoveAddSomeItems(item)).ParamName);
            Assert.Equal("item", Assert.Throws<ArgumentOutOfRangeException>(() => zdd.RemoveAddSomeItems(0, item)).ParamName);
        }

        [Fact]
        public void AFamilyFromAnotherManagerIsRejected()
        {
            using ZddManager one = new ZddManager(4);
            using ZddManager other = new ZddManager(4);

            Zdd foreign = other.Singleton(0);

            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.RemoveSomeItem(foreign, [1])).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.AddSomeItem(foreign, [1])).ParamName);
            Assert.Equal("f", Assert.Throws<ArgumentException>(() => one.RemoveAddSomeItems(foreign, [1])).ParamName);
        }

        [Fact]
        public void OperationsOnADisposedManagerThrow()
        {
            ZddManager manager = new ZddManager(4);
            Zdd zdd = manager.Singleton(1);
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => zdd.RemoveSomeItem());
            Assert.Throws<ObjectDisposedException>(() => zdd.RemoveSomeItem(1));
            Assert.Throws<ObjectDisposedException>(() => zdd.AddSomeItem());
            Assert.Throws<ObjectDisposedException>(() => zdd.AddSomeItem(1));
            Assert.Throws<ObjectDisposedException>(() => zdd.RemoveAddSomeItems());
            Assert.Throws<ObjectDisposedException>(() => zdd.RemoveAddSomeItems(1));
        }

        // ---- items の候補 ----

        /// <summary>空・単独・全部・逆順・重複ありを一通り混ぜた items の候補（<c>ExtremalOperationTests.ItemSubsetCases</c> と同じ流儀）。</summary>
        private static IEnumerable<int[]> ItemSubsetCases(int variableCount, int seed)
        {
            yield return [];

            if (variableCount == 0)
            {
                yield break;
            }

            int[] all = Enumerable.Range(0, variableCount).ToArray();

            yield return [0];
            yield return [all[^1]];
            yield return all;
            yield return all.Reverse().ToArray();

            if (variableCount >= 2)
            {
                yield return [0, 0, 0];
                yield return all.Where(item => item % 2 == 0).ToArray();
            }

            uint state = (uint)seed + 0x9E3779B9u;

            for (int i = 0; i < 4; i++)
            {
                int[] order = (int[])all.Clone();

                for (int j = order.Length - 1; j > 0; j--)
                {
                    state = (state * 1664525u) + 1013904223u;
                    int k = (int)(state % (uint)(j + 1));
                    (order[j], order[k]) = (order[k], order[j]);
                }

                state = (state * 1664525u) + 1013904223u;
                int size = (int)(state % (uint)(variableCount + 1));

                yield return order[..size];
            }
        }
    }
}

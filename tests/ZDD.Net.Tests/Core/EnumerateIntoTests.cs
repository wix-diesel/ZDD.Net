using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// アロケーションなしの列挙（<see cref="Zdd.EnumerateInto"/> / <see cref="SetSpanEnumerator"/>）と
    /// その必要バッファ長（<see cref="Zdd.MaxSetSize"/>）の検証（M6-2、issue #137）。
    /// </summary>
    /// <remarks>
    /// 照合相手は既存の <see cref="Zdd.Sets"/>: 同じ族・同じ順序なら要素も並びも完全に一致するはず
    /// （設計上、両者は同じ深さ優先探索の 2 つの表現でしかない）。<see cref="EnumerationTests"/> が
    /// 素朴実装との照合を担当済みなので、ここでは <c>Sets()</c> との一致に絞る。
    /// </remarks>
    public class EnumerateIntoTests
    {
        /// <summary>列挙の照合に使う変数の個数の上限（docs/ROADMAP.md M6-2、変数 &#8804; 16 全網羅）。</summary>
        private const int MaxEnumerationVariableCount = BruteForceFamily.MaxPowerSetVariableCount;

        /// <summary>スタックオーバーフローの回帰テストで使う変数の個数（docs/PLAN.md &#167;4.5）。</summary>
        private const int DeepVariableCount = 100_000;

        // ---- MaxSetSize ----

        [Fact]
        public void TerminalFamiliesHaveTheirDefinedMaxSetSize()
        {
            using ZddManager manager = new ZddManager(4);

            // ∅ には集合が 1 つも無いが、0 を返す約束（バッファ 0 で正しく回る）。
            Assert.Equal(0, manager.Empty.MaxSetSize);

            // {∅} は空集合だけなので最大要素数も 0。
            Assert.Equal(0, manager.Base.MaxSetSize);

            // 1 要素集合だけの族。
            Assert.Equal(1, manager.Singleton(2).MaxSetSize);
        }

        [Fact]
        public void MaxSetSizeMatchesCountBySizeLengthMinusOneUpToSixteenVariables()
        {
            for (int variableCount = 0; variableCount <= MaxEnumerationVariableCount; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                foreach (Zdd zdd in NonEmptyFamilies(manager, variableCount))
                {
                    Assert.Equal(zdd.CountBySize().Length - 1, zdd.MaxSetSize);
                }

                // 空族だけは CountBySize() が長さ 0 の配列を返すので、この等式に乗らない。
                // MaxSetSize 自身は「空族は 0」という別の約束を守る（TerminalFamiliesHaveTheirDefinedMaxSetSize）。
                Assert.Equal(0, manager.Empty.MaxSetSize);
            }
        }

        [Fact]
        public void MaxSetSizeOnInvalidHandlesThrowsWhereOtherEvaluationsDo()
        {
            using ZddManager manager = new ZddManager(4);
            Zdd family = manager.Singleton(1);
            Zdd invalid = default;

            Assert.Throws<InvalidOperationException>(() => invalid.MaxSetSize);

            manager.Dispose();
            Assert.Throws<ObjectDisposedException>(() => family.MaxSetSize);
        }

        // ---- Sets() との一致 ----

        [Fact]
        public void EnumerateIntoMatchesSetsUpToSixteenVariables()
        {
            for (int variableCount = 0; variableCount <= MaxEnumerationVariableCount; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                AssertMatchesSets(manager.Empty);
                AssertMatchesSets(manager.Base);
                AssertMatchesSets(PowerSetOf(manager));

                foreach (BruteForceFamily family in FamilyCases.RandomFamilies(variableCount, 8, seed: 1600 + variableCount))
                {
                    AssertMatchesSets(ZddFamilies.Build(manager, family));
                }
            }
        }

        [Fact]
        public void EnumerateIntoAcceptsABufferLargerThanMaxSetSize()
        {
            using ZddManager manager = new ZddManager(6);
            Zdd family = ZddFamilies.Build(manager, new[] { 0, 3 }, new[] { 1 });

            int[] buffer = new int[family.MaxSetSize + 5];

            List<int[]> collected = new List<int[]>();
            SetSpanEnumerator enumerator = family.EnumerateInto(buffer);
            while (enumerator.MoveNext())
            {
                collected.Add(enumerator.Current.ToArray());
            }

            Assert.Equal(
                family.Sets().Select(Key).ToArray(),
                collected.Select(Key).ToArray());
        }

        [Fact]
        public void EnumerateIntoWorksWithAZeroLengthBufferForTheEmptyAndBaseFamilies()
        {
            using ZddManager manager = new ZddManager(5);

            Assert.Equal(0, manager.Empty.MaxSetSize);
            Assert.Equal(0, manager.Base.MaxSetSize);

            SetSpanEnumerator empty = manager.Empty.EnumerateInto(Span<int>.Empty);
            Assert.False(empty.MoveNext());

            SetSpanEnumerator @base = manager.Base.EnumerateInto(Span<int>.Empty);
            Assert.True(@base.MoveNext());
            Assert.True(@base.Current.IsEmpty);
            Assert.False(@base.MoveNext());
        }

        [Fact]
        public void ForeachWorksAgainstEnumerateInto()
        {
            using ZddManager manager = new ZddManager(4);
            Zdd powerSet = PowerSetOf(manager);

            int[] buffer = new int[powerSet.MaxSetSize];
            List<int[]> collected = new List<int[]>();

            foreach (ReadOnlySpan<int> set in powerSet.EnumerateInto(buffer))
            {
                collected.Add(set.ToArray());
            }

            Assert.Equal(
                powerSet.Sets().Select(Key).ToArray(),
                collected.Select(Key).ToArray());
        }

        [Fact]
        public void BothOrdersMatchTheirSetsCounterpart()
        {
            using ZddManager manager = new ZddManager(4);
            Zdd powerSet = PowerSetOf(manager);
            int[] buffer = new int[powerSet.MaxSetSize];

            foreach (ZddEnumerationOrder order in new[] { ZddEnumerationOrder.Default, ZddEnumerationOrder.Lexicographic })
            {
                List<int[]> collected = new List<int[]>();
                SetSpanEnumerator enumerator = powerSet.EnumerateInto(buffer, order);
                while (enumerator.MoveNext())
                {
                    collected.Add(enumerator.Current.ToArray());
                }

                Assert.Equal(
                    powerSet.Sets(order).Select(Key).ToArray(),
                    collected.Select(Key).ToArray());
            }
        }

        // ---- Current の使い回し ----

        [Fact]
        public void CurrentIsOverwrittenByTheNextMoveNext()
        {
            using ZddManager manager = new ZddManager(4);
            Zdd powerSet = PowerSetOf(manager);
            int[] buffer = new int[powerSet.MaxSetSize];

            SetSpanEnumerator enumerator = powerSet.EnumerateInto(buffer, ZddEnumerationOrder.Lexicographic);

            Assert.True(enumerator.MoveNext());
            Assert.True(enumerator.Current.IsEmpty);

            Assert.True(enumerator.MoveNext());
            int[] second = enumerator.Current.ToArray();
            Assert.Equal(new[] { 0 }, second);

            Assert.True(enumerator.MoveNext());

            // コピーは変わらない。バッファ（引いては前回の Current）は書き換わった。
            Assert.Equal(new[] { 0 }, second);
            Assert.NotEqual(second, enumerator.Current.ToArray());
        }

        // ---- バッファ長の検証 ----

        [Fact]
        public void ABufferShorterThanMaxSetSizeIsRejectedEagerly()
        {
            using ZddManager manager = new ZddManager(6);
            Zdd family = ZddFamilies.Build(manager, new[] { 0, 3 }, new[] { 1 });

            Assert.True(family.MaxSetSize >= 1);

            ArgumentException error = Assert.Throws<ArgumentException>(
                () => family.EnumerateInto(new int[family.MaxSetSize - 1]));
            Assert.Equal("buffer", error.ParamName);
        }

        [Fact]
        public void ABufferExactlyMaxSetSizeLongWorks()
        {
            using ZddManager manager = new ZddManager(6);
            Zdd family = ZddFamilies.Build(manager, new[] { 0, 3 }, new[] { 1 });

            int[] buffer = new int[family.MaxSetSize];

            List<int[]> collected = new List<int[]>();
            SetSpanEnumerator enumerator = family.EnumerateInto(buffer);
            while (enumerator.MoveNext())
            {
                collected.Add(enumerator.Current.ToArray());
            }

            Assert.Equal(
                family.Sets().Select(Key).ToArray(),
                collected.Select(Key).ToArray());
        }

        // ---- 誤用 ----

        [Fact]
        public void AnUndefinedOrderIsRejectedWhereItIsAskedForNotWhereItIsEnumerated()
        {
            using ZddManager manager = new ZddManager(3);
            Zdd powerSet = PowerSetOf(manager);
            int[] buffer = new int[powerSet.MaxSetSize];

            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
                () => powerSet.EnumerateInto(buffer, (ZddEnumerationOrder)7));
            Assert.Equal("order", error.ParamName);
        }

        [Fact]
        public void EnumerateIntoOnADefaultHandleThrowsWhereItIsCalled()
        {
            Zdd invalid = default;

            Assert.Throws<InvalidOperationException>(() => invalid.EnumerateInto(Span<int>.Empty));
        }

        [Fact]
        public void EnumerateIntoOnADisposedManagerThrowsWhereItIsCalled()
        {
            ZddManager manager = new ZddManager(4);
            Zdd family = PowerSetOf(manager);
            int[] buffer = new int[family.MaxSetSize];
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => family.EnumerateInto(buffer));
        }

        // ---- 深い ZDD（docs/PLAN.md §4.5 の回帰テスト）----

        [Fact]
        [Trait("Category", "Slow")]
        public void ADeepFamilyDoesNotOverflowTheStack()
        {
            using ZddManager manager = new ZddManager(DeepVariableCount);

            // 変数 10 万個すべてを含む集合 1 つだけの族。ノードが 10 万段に連なる。
            Zdd single = SingleFullSet(manager);

            Assert.Equal(DeepVariableCount, single.MaxSetSize);

            int[] buffer = new int[single.MaxSetSize];
            SetSpanEnumerator enumerator = single.EnumerateInto(buffer);

            Assert.True(enumerator.MoveNext());
            Assert.Equal(DeepVariableCount, enumerator.Current.Length);
            Assert.Equal(0, enumerator.Current[0]);
            Assert.Equal(DeepVariableCount - 1, enumerator.Current[^1]);
            Assert.False(enumerator.MoveNext());
        }

        // ---- アロケーション ----

        [Fact]
        public void TheMoveNextLoopDoesNotAllocate()
        {
            // 明示スタック/チェーンの内部配列は列挙子ごとに初期容量（32/16）から新しく確保され、
            // 前の列挙子で伸びた分は引き継がれない。だから「事前に 1 回 JIT を通す」だけでは
            // 伸長そのものは避けられず、深さが初期容量に収まる変数数を選ぶ必要がある
            // （変数 5 個から明示スタックが 1 回伸びることを確認済み）。
            // EnumerateInto 自体（MaxSetSize の評価と内部配列の確保）は計測区間の外で行う。
            // 測りたいのは MoveNext のループであって、列挙子の構築コストではない。
            using ZddManager manager = new ZddManager(4);
            Zdd powerSet = PowerSetOf(manager);
            int[] buffer = new int[powerSet.MaxSetSize];

            // 先に JIT を通しておく。測るのは定常状態のアロケーション。
            SetSpanEnumerator warm = powerSet.EnumerateInto(buffer, ZddEnumerationOrder.Lexicographic);
            while (warm.MoveNext())
            {
                _ = warm.Current;
            }

            SetSpanEnumerator enumerator = powerSet.EnumerateInto(buffer, ZddEnumerationOrder.Lexicographic);

            long before = GC.GetAllocatedBytesForCurrentThread();
            while (enumerator.MoveNext())
            {
                _ = enumerator.Current;
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.Equal(0L, after - before);
        }

        // ---- 補助 ----

        /// <summary><paramref name="zdd"/> を両方の順序で <see cref="Zdd.EnumerateInto"/> と <see cref="Zdd.Sets"/> で辿り、完全一致することを確かめる。</summary>
        private static void AssertMatchesSets(Zdd zdd)
        {
            foreach (ZddEnumerationOrder order in new[] { ZddEnumerationOrder.Default, ZddEnumerationOrder.Lexicographic })
            {
                int[] buffer = new int[zdd.MaxSetSize];

                List<int[]> viaSpan = new List<int[]>();
                SetSpanEnumerator enumerator = zdd.EnumerateInto(buffer, order);
                while (enumerator.MoveNext())
                {
                    viaSpan.Add(enumerator.Current.ToArray());
                }

                int[][] viaSets = zdd.Sets(order).ToArray();

                Assert.Equal(viaSets.Select(Key).ToArray(), viaSpan.Select(Key).ToArray());
            }
        }

        /// <summary>境界（∅ / {∅} / 冪集合）とランダムな族。空族は <see cref="CountBySize"/> との照合対象外なので別扱い。</summary>
        private static IEnumerable<Zdd> NonEmptyFamilies(ZddManager manager, int variableCount)
        {
            yield return manager.Base;
            yield return PowerSetOf(manager);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(variableCount, 6, seed: 1610 + variableCount))
            {
                if (!family.IsEmpty)
                {
                    yield return ZddFamilies.Build(manager, family);
                }
            }
        }

        /// <summary>集合を並びごと比べられる文字列に直す（アサーションの読みやすさのため）。</summary>
        private static string Key(int[] set) => string.Join(",", set);

        /// <summary>全変数の冪集合 <c>2^U</c>。ノードは変数の個数ぶんしかない。</summary>
        private static Zdd PowerSetOf(ZddManager manager)
        {
            Zdd result = manager.Base;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                result = manager.CreateNode(item, result, result);
            }

            return result;
        }

        /// <summary>全変数を含む集合 1 つだけの族 <c>{{0, …, n-1}}</c>。</summary>
        private static Zdd SingleFullSet(ZddManager manager)
        {
            Zdd result = manager.Base;

            for (int item = manager.VariableCount - 1; item >= 0; item--)
            {
                result = manager.CreateNode(item, manager.Empty, result);
            }

            return result;
        }
    }
}

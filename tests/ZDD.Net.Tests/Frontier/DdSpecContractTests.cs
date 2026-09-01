using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Frontier;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// スペックのインタフェース（<see cref="IDdSpec{TState}"/> ほか）の規約が、
    /// 実際に書いたスペックで成り立つことを確かめる。
    /// </summary>
    /// <remarks>
    /// 構築器は M2-4 で入るので、ここでは <see cref="SpecWalker"/>（総当たりの深さ優先）で
    /// スペックを歩き、受理する集合が手計算と一致することを見る。
    /// <c>docs/frontier-spec-guide.md</c> に載せている例をそのまま置いてあり、
    /// ドキュメントのコードが実在することの担保も兼ねている。
    /// </remarks>
    public class DdSpecContractTests
    {
        [Theory]
        [InlineData(4, 0)]
        [InlineData(4, 1)]
        [InlineData(4, 2)]
        [InlineData(4, 4)]
        [InlineData(6, 3)]
        [InlineData(7, 5)]
        public void ExactlyKSpecAcceptsEverySubsetOfSizeK(int itemCount, int k)
        {
            List<int[]> accepted =
                SpecWalker.Accepted<ExactlyKSpec, int>(new ExactlyKSpec(itemCount, k), itemCount);

            int[][] expected = AllSubsets(itemCount).Where(subset => subset.Length == k).ToArray();

            Assert.Equal(AsText(expected), AsText(accepted));
        }

        /// <summary>k がアイテムの個数を超えるときは、先読み枝刈りが全部の枝を ⊥ に落とす。</summary>
        [Fact]
        public void ExactlyKSpecAcceptsNothingWhenKExceedsTheItemCount()
        {
            Assert.Empty(SpecWalker.Accepted<ExactlyKSpec, int>(new ExactlyKSpec(3, 4), 3));
        }

        [Fact]
        public void AnArraySpecCanBeWrittenAgainstSpansOfState()
        {
            AtMostOnePerPairSpec spec = new AtMostOnePerPairSpec(pairCount: 2);
            int[] state = new int[spec.ArrayLength];

            Assert.Equal(4, spec.GetRoot(state));

            // item 0（level 4）を入れると、対になる item 1（level 3）は入れられなくなる。
            Assert.Equal(3, spec.GetChild(state, level: 4, value: 1));
            Assert.Equal(DdResult.False, spec.GetChild(state, level: 3, value: 1));
        }

        [Fact]
        public void AHybridSpecCanBeWrittenAgainstAScalarAndASpanOfState()
        {
            AtMostOnePerPairWithBudgetSpec spec = new AtMostOnePerPairWithBudgetSpec(pairCount: 2, budget: 1);
            int[] array = new int[spec.ArrayLength];
            int taken = 0;

            Assert.Equal(4, spec.GetRoot(ref taken, array));
            Assert.Equal(3, spec.GetChild(ref taken, array, level: 4, value: 1));

            // 予算を使い切ったので、以降どのアイテムも入れられない。
            Assert.Equal(DdResult.False, spec.GetChild(ref taken, array, level: 3, value: 1));
            Assert.True(spec.ScalarEquals(taken, 1));
            Assert.Equal(spec.ScalarHashCode(1), spec.ScalarHashCode(taken));
        }

        private static IEnumerable<int[]> AllSubsets(int itemCount)
        {
            for (int mask = 0; mask < 1 << itemCount; mask++)
            {
                yield return Enumerable.Range(0, itemCount).Where(item => (mask & (1 << item)) != 0).ToArray();
            }
        }

        /// <summary>集合の族を、順序に依存しない比較ができるテキストにする。</summary>
        private static string AsText(IEnumerable<int[]> family) =>
            string.Join(" ", family.Select(set => "{" + string.Join(",", set) + "}").OrderBy(text => text, StringComparer.Ordinal));

        /// <summary>
        /// ちょうど k 個のアイテムを選ぶ集合の族（<c>docs/frontier-spec-guide.md</c> §5 の例）。
        /// </summary>
        private readonly struct ExactlyKSpec : IDdSpec<int>
        {
            private readonly int _itemCount;
            private readonly int _k;

            public ExactlyKSpec(int itemCount, int k)
            {
                _itemCount = itemCount;
                _k = k;
            }

            public int GetRoot(ref int taken)
            {
                taken = 0;

                return _itemCount;
            }

            public int GetChild(ref int taken, int level, int value)
            {
                taken += value;

                if (taken > _k)
                {
                    return DdResult.False;
                }

                if (taken == _k)
                {
                    return DdResult.True;
                }

                int remaining = level - 1;

                return taken + remaining < _k ? DdResult.False : remaining;
            }

            public bool StateEquals(in int left, in int right) => left == right;

            public int StateHashCode(in int state) => state;
        }

        /// <summary>
        /// アイテムを 2 個ずつの組に分け、各組から高々 1 個を選ぶ族。状態は「組ごとに選んだかどうか」。
        /// </summary>
        /// <remarks>状態の大きさが実行時に決まる（組の個数ぶん）ので <see cref="IArrayDdSpec"/> で書く。</remarks>
        private readonly struct AtMostOnePerPairSpec : IArrayDdSpec
        {
            private readonly int _pairCount;

            public AtMostOnePerPairSpec(int pairCount)
            {
                _pairCount = pairCount;
            }

            public int ArrayLength => _pairCount;

            public int GetRoot(Span<int> state)
            {
                state.Clear();

                return _pairCount == 0 ? DdResult.True : 2 * _pairCount;
            }

            public int GetChild(Span<int> state, int level, int value)
            {
                int item = (2 * _pairCount) - level;
                int pair = item / 2;

                if (value == 1)
                {
                    if (state[pair] != 0)
                    {
                        return DdResult.False;
                    }

                    state[pair] = 1;
                }

                return level == 1 ? DdResult.True : level - 1;
            }
        }

        /// <summary>
        /// <see cref="AtMostOnePerPairSpec"/> に「選べるのは全部で budget 個まで」を足した族。
        /// </summary>
        /// <remarks>組ごとの情報は配列、全体の個数はスカラ。<see cref="IHybridDdSpec{TScalar}"/> の典型形。</remarks>
        private readonly struct AtMostOnePerPairWithBudgetSpec : IHybridDdSpec<int>
        {
            private readonly int _pairCount;
            private readonly int _budget;

            public AtMostOnePerPairWithBudgetSpec(int pairCount, int budget)
            {
                _pairCount = pairCount;
                _budget = budget;
            }

            public int ArrayLength => _pairCount;

            public int GetRoot(ref int taken, Span<int> array)
            {
                taken = 0;
                array.Clear();

                return _pairCount == 0 ? DdResult.True : 2 * _pairCount;
            }

            public int GetChild(ref int taken, Span<int> array, int level, int value)
            {
                int item = (2 * _pairCount) - level;
                int pair = item / 2;

                if (value == 1)
                {
                    if (array[pair] != 0 || taken == _budget)
                    {
                        return DdResult.False;
                    }

                    array[pair] = 1;
                    taken++;
                }

                return level == 1 ? DdResult.True : level - 1;
            }

            public bool ScalarEquals(in int left, in int right) => left == right;

            public int ScalarHashCode(in int scalar) => scalar;
        }
    }
}

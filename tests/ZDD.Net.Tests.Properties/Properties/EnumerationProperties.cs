using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using Xunit.Abstractions;
using ZDD.Net.Core;
using ZDD.Net.Tests.Properties.Harness;

namespace ZDD.Net.Tests.Properties.Properties
{
    /// <summary>
    /// 列挙（<c>Sets</c>）とメンバシップ（<c>Contains</c> / <c>IsSubsetOf</c> / <c>Overlaps</c>）が
    /// 満たすべき性質。
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="ZddSets.ToMasks"/>（<c>OnSet</c> / <c>OffSet</c> だけで族を読み出す、
    /// 列挙とは独立な経路）と、族を実際に作る演算（<c>-</c> / <c>&amp;</c>）である。
    /// 短絡版の <c>IsSubsetOf</c> / <c>Overlaps</c> は「族を作らずに同じ答を出す」ことが売りなので、
    /// 作る版との一致こそが性質になる。
    /// </remarks>
    public class EnumerationProperties
    {
        private readonly ITestOutputHelper _output;

        public EnumerationProperties(ITestOutputHelper output) => _output = output;

        [Fact]
        public void EnumerationReturnsExactlyTheSetsOfTheFamily() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    int[] expected = ZddSets.ToMasks(family);

                    foreach (ZddEnumerationOrder order in Orders)
                    {
                        int[] actual = MasksOf(family, order);

                        // 重複が無いこと（並べ替える前に個数で見ないと、重複が潰れて見えなくなる）。
                        Assert.Equal(actual.Length, actual.Distinct().Count());

                        // 個数が濃度と一致し、中身が族そのものと一致すること。
                        Assert.Equal(new BigInteger(actual.Length), family.Count);
                        Assert.Equal(expected, actual.OrderBy(mask => mask).ToArray());
                    }
                },
                _output);

        [Fact]
        public void EachOrderReturnsTheSetsSorted() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    AssertSorted(family, ZddEnumerationOrder.Default, CompareIndicatorVectors);
                    AssertSorted(family, ZddEnumerationOrder.Lexicographic, CompareItemSequences);
                },
                _output);

        [Fact]
        public void ContainsIsTrueExactlyForTheEnumeratedSets() =>
            PropertyCheck.Sample(
                FamilyGen.Family,
                spec =>
                {
                    using ZddManager manager = new ZddManager(spec.VariableCount);
                    Zdd family = spec.Build(manager);

                    HashSet<int> members = new HashSet<int>(MasksOf(family, ZddEnumerationOrder.Default));

                    // 生成される宇宙は小さい（FamilyGen.MaxVariableCount）ので、全部分集合を試せる。
                    for (int mask = 0; mask < 1 << spec.VariableCount; mask++)
                    {
                        Assert.Equal(members.Contains(mask), family.Contains(FamilySpec.ItemsOf(mask)));
                    }
                },
                _output);

        [Fact]
        public void IsSubsetOfAgreesWithTheDifferenceBeingEmpty() =>
            PropertyCheck.Sample(
                FamilyGen.Pair,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.First.VariableCount);
                    Zdd f = input.First.Build(manager);
                    Zdd g = input.Second.Build(manager);

                    // 族を作らない短絡版が、作る版と同じ答であること。
                    Assert.Equal((f - g).IsEmpty, f.IsSubsetOf(g));

                    // 半順序であること: 反射的で、両向きに成り立つのは等しいときだけ。
                    Assert.True(f.IsSubsetOf(f));
                    Assert.Equal(f == g, f.IsSubsetOf(g) && g.IsSubsetOf(f));

                    // 和は上に、交わりは下にある。
                    Assert.True(f.IsSubsetOf(f | g));
                    Assert.True((f & g).IsSubsetOf(f));
                },
                _output);

        [Fact]
        public void OverlapsAgreesWithTheIntersectionBeingNonEmpty() =>
            PropertyCheck.Sample(
                FamilyGen.Pair,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.First.VariableCount);
                    Zdd f = input.First.Build(manager);
                    Zdd g = input.Second.Build(manager);

                    Assert.Equal(!(f & g).IsEmpty, f.Overlaps(g));

                    // 対称であること、空の族とは決して交わらないこと。
                    Assert.Equal(f.Overlaps(g), g.Overlaps(f));
                    Assert.False(f.Overlaps(manager.Empty));

                    // 交わる ⇔ 交わりが空でない ⇔ 交わりに属する集合がある。
                    Assert.Equal(f.Overlaps(g), (f & g).Count > BigInteger.Zero);
                },
                _output);

        [Fact]
        public void ComparisonsSurviveTheDetourThroughAnItem() =>
            PropertyCheck.Sample(
                FamilyGen.FamilyAndItem,
                input =>
                {
                    using ZddManager manager = new ZddManager(input.Family.VariableCount);
                    Zdd family = input.Family.Build(manager);
                    int item = input.Item;

                    // item の有無で分けた 2 つは、どちらも元の族に含まれ、互いに交わらない。
                    Zdd withItem = family - family.OffSet(item);
                    Zdd withoutItem = family.OffSet(item);

                    Assert.True(withItem.IsSubsetOf(family));
                    Assert.True(withoutItem.IsSubsetOf(family));
                    Assert.False(withItem.Overlaps(withoutItem));

                    // 2 つを合わせれば元に戻る。
                    Assert.True(family.IsSubsetOf(withItem | withoutItem));
                },
                _output);

        private static readonly ZddEnumerationOrder[] Orders =
        {
            ZddEnumerationOrder.Default,
            ZddEnumerationOrder.Lexicographic,
        };

        /// <summary>列挙した集合をビットマスクに直す（列挙の順序はそのまま残す）。</summary>
        private static int[] MasksOf(in Zdd family, ZddEnumerationOrder order)
        {
            List<int> masks = new List<int>();

            foreach (int[] set in family.Sets(order))
            {
                int mask = 0;
                foreach (int item in set)
                {
                    mask |= 1 << item;
                }

                masks.Add(mask);
            }

            return masks.ToArray();
        }

        /// <summary>列挙の並びが <paramref name="comparison"/> の昇順であること。</summary>
        private static void AssertSorted(in Zdd family, ZddEnumerationOrder order, Comparison<int> comparison)
        {
            int[] actual = MasksOf(family, order);

            int[] sorted = actual.ToArray();
            Array.Sort(sorted, comparison);

            Assert.Equal(sorted, actual);
        }

        /// <summary>指示ベクトルの辞書順（食い違う最小の item を含まない方が先）。</summary>
        private static int CompareIndicatorVectors(int left, int right)
        {
            if (left == right)
            {
                return 0;
            }

            int lowestDifference = 1 << BitOperations.TrailingZeroCount((uint)(left ^ right));
            return (left & lowestDifference) == 0 ? -1 : 1;
        }

        /// <summary>昇順の item 列としての辞書順（短い方が接頭辞なら先）。</summary>
        private static int CompareItemSequences(int left, int right)
        {
            int[] leftItems = FamilySpec.ItemsOf(left).ToArray();
            int[] rightItems = FamilySpec.ItemsOf(right).ToArray();

            for (int i = 0; i < Math.Min(leftItems.Length, rightItems.Length); i++)
            {
                if (leftItems[i] != rightItems[i])
                {
                    return leftItems[i] < rightItems[i] ? -1 : 1;
                }
            }

            return leftItems.Length.CompareTo(rightItems.Length);
        }
    }
}

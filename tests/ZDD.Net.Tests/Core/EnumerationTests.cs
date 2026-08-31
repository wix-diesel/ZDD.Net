using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Core
{
    /// <summary>
    /// 列挙（<see cref="Zdd.GetEnumerator"/> / <see cref="Zdd.Sets"/>）とメンバシップ
    /// （<see cref="Zdd.Contains(IEnumerable{int})"/> / <see cref="Zdd.IsSubsetOf"/> /
    /// <see cref="Zdd.Overlaps"/>）の検証。
    /// </summary>
    /// <remarks>
    /// 照合相手は <see cref="BruteForceFamily"/>（族の集合をビットマスクで実体として持つ素朴実装）。
    /// 列挙は「数えた結果を実際に取り出す」API であると同時に、族の中身を外から確かめる唯一の手段
    /// でもあるので、個数（<see cref="Zdd.Count"/>）・並び・重複の無さをそれぞれ別に確かめる。
    /// </remarks>
    public class EnumerationTests
    {
        /// <summary>列挙と濃度の照合に使う変数の個数の上限（docs/ROADMAP.md M1-13）。</summary>
        private const int MaxEnumerationVariableCount = BruteForceFamily.MaxPowerSetVariableCount;

        /// <summary>スタックオーバーフローの回帰テストで使う変数の個数（docs/PLAN.md §4.5）。</summary>
        private const int DeepVariableCount = 100_000;

        // ---- 境界 ----

        [Fact]
        public void TerminalFamiliesEnumerateWhatTheirDefinitionSays()
        {
            using ZddManager manager = new ZddManager(4);

            // ∅ は集合を 1 つも持たない。
            Assert.Empty(manager.Empty);

            // {∅} は空集合を 1 つだけ持つ。「1 つも無い」との違いがここに出る。
            int[][] baseSets = manager.Base.ToArray();
            Assert.Single(baseSets);
            Assert.Empty(baseSets[0]);

            // 1 要素集合だけの族。
            Assert.Equal(new[] { new[] { 2 } }, manager.Singleton(2).ToArray());
        }

        [Fact]
        public void AManagerWithoutVariablesStillEnumeratesItsTwoFamilies()
        {
            using ZddManager manager = new ZddManager(0);

            Assert.Empty(manager.Empty);
            Assert.Equal(new[] { Array.Empty<int>() }, manager.Base.ToArray());
        }

        // ---- 素朴実装との照合 ----

        [Fact]
        public void EveryFamilyOfThreeVariablesEnumeratesLikeTheNaiveImplementation()
        {
            const int VariableCount = 3;

            using ZddManager manager = new ZddManager(VariableCount);

            // 3 変数の族は 2^8 = 256 通り。すべて試せる。
            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertEnumerationMatchesNaive(manager, family);
            }
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void EveryFamilyOfFourVariablesEnumeratesLikeTheNaiveImplementation()
        {
            const int VariableCount = FamilyCases.AllFamiliesVariableLimit;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.AllFamilies(VariableCount))
            {
                AssertEnumerationMatchesNaive(manager, family);
            }
        }

        [Fact]
        public void EnumerationMatchesTheNaiveFamilyUpToSixteenVariables()
        {
            for (int variableCount = 0; variableCount <= MaxEnumerationVariableCount; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                // 境界（∅ / {∅} / 冪集合）と、ランダムな族を混ぜて回す。
                AssertEnumerationMatchesNaive(manager, BruteForceFamily.Empty(variableCount));
                AssertEnumerationMatchesNaive(manager, BruteForceFamily.Base(variableCount));
                AssertEnumerationMatchesNaive(manager, BruteForceFamily.PowerSet(variableCount));

                foreach (BruteForceFamily family in FamilyCases.RandomFamilies(variableCount, 8, seed: 1400 + variableCount))
                {
                    AssertEnumerationMatchesNaive(manager, family);
                }
            }
        }

        // ---- 順序 ----

        [Fact]
        public void TheDefaultOrderIsTheLexicographicOrderOfTheIndicatorVectors()
        {
            using ZddManager manager = new ZddManager(3);

            // item 0 を含まない集合が先で、そのなかでは item 1 を含まない方が先。
            Assert.Equal(
                new[]
                {
                    Array.Empty<int>(),
                    new[] { 2 },
                    new[] { 1 },
                    new[] { 1, 2 },
                    new[] { 0 },
                    new[] { 0, 2 },
                    new[] { 0, 1 },
                    new[] { 0, 1, 2 },
                },
                PowerSetOf(manager).ToArray());
        }

        [Fact]
        public void TheLexicographicOrderIsTheLexicographicOrderOfTheItemSequences()
        {
            using ZddManager manager = new ZddManager(3);

            // 空列はどの列の接頭辞でもあるので最小。以降は先頭の item が小さい順。
            Assert.Equal(
                new[]
                {
                    Array.Empty<int>(),
                    new[] { 0 },
                    new[] { 0, 1 },
                    new[] { 0, 1, 2 },
                    new[] { 0, 2 },
                    new[] { 1 },
                    new[] { 1, 2 },
                    new[] { 2 },
                },
                PowerSetOf(manager).Sets(ZddEnumerationOrder.Lexicographic).ToArray());
        }

        [Fact]
        public void BothOrdersAreTotalOrdersOnEveryFamilyUpToTwelveVariables()
        {
            for (int variableCount = 0; variableCount <= FamilyCases.ExhaustiveVariableLimit; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                foreach (BruteForceFamily family in Families(variableCount, seed: 1410 + variableCount))
                {
                    Zdd zdd = ZddFamilies.Build(manager, family);

                    AssertOrder(zdd, family, ZddEnumerationOrder.Default, CompareIndicatorVectors);
                    AssertOrder(zdd, family, ZddEnumerationOrder.Lexicographic, CompareItemSequences);
                }
            }
        }

        [Fact]
        public void TheTwoOrdersHoldTheSameSetsAndDisagreeOnTheirPlaces()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd powerSet = PowerSetOf(manager);
            int[][] natural = powerSet.Sets(ZddEnumerationOrder.Default).ToArray();
            int[][] lexicographic = powerSet.Sets(ZddEnumerationOrder.Lexicographic).ToArray();

            // 中身は同じ族なのだから、集合として見れば一致する。
            Assert.Equal(
                natural.Select(Key).OrderBy(text => text, StringComparer.Ordinal),
                lexicographic.Select(Key).OrderBy(text => text, StringComparer.Ordinal));

            // 並びは違う（{0,2} と {1} の前後が入れ替わる）。
            Assert.NotEqual(natural.Select(Key), lexicographic.Select(Key));
        }

        [Fact]
        public void AnUndefinedOrderIsRejectedWhereItIsAskedForNotWhereItIsEnumerated()
        {
            using ZddManager manager = new ZddManager(3);

            Zdd powerSet = PowerSetOf(manager);

            // 検査は Sets() を呼んだその場で行う（列挙を始めてからではない）。
            ArgumentOutOfRangeException error =
                Assert.Throws<ArgumentOutOfRangeException>(() => powerSet.Sets((ZddEnumerationOrder)7));
            Assert.Equal("order", error.ParamName);
        }

        // ---- 遅延であること ----

        [Fact]
        public void EnumerationIsLazyEnoughToTakeTheFirstFewSetsOfAHugeFamily()
        {
            const int VariableCount = 200;

            using ZddManager manager = new ZddManager(VariableCount);

            Zdd powerSet = PowerSetOf(manager);

            // 2^200 個の集合を持つ族。全部並べることは決してできないが、先頭の 10 個なら取れる。
            Assert.Equal(BigInteger.Pow(2, VariableCount), powerSet.Count);

            Stopwatch watch = Stopwatch.StartNew();
            int[][] first = powerSet.Take(10).ToArray();
            watch.Stop();

            Assert.Equal(10, first.Length);

            // 先頭は空集合、次は「いちばん葉側の item だけ」（0-枝優先の順）。
            Assert.Empty(first[0]);
            Assert.Equal(new[] { VariableCount - 1 }, first[1]);

            // 族の大きさに引きずられていないことの目安。走査したのは高々 10 本の経路である。
            Assert.True(
                watch.Elapsed < TimeSpan.FromSeconds(5),
                $"Taking 10 sets from a family of 2^{VariableCount} took {watch.Elapsed}, which suggests it is not lazy.");

            // 辞書順でも同じく遅延で、先頭は空集合、次は item 0 から始まる集合。
            int[][] lexicographic = powerSet.Sets(ZddEnumerationOrder.Lexicographic).Take(3).ToArray();
            Assert.Empty(lexicographic[0]);
            Assert.Equal(0, lexicographic[1][0]);
        }

        [Fact]
        public void TheSameEnumerableCanBeWalkedMoreThanOnce()
        {
            using ZddManager manager = new ZddManager(5);

            Zdd family = ZddFamilies.Build(manager, new[] { 0, 3 }, new[] { 1 }, Array.Empty<int>());
            IEnumerable<int[]> sets = family.Sets();

            // 族は不変なので、何度辿っても同じ並びが返る。
            Assert.Equal(sets.Select(Key).ToArray(), sets.Select(Key).ToArray());
        }

        // ---- 返る配列 ----

        [Fact]
        public void EveryEnumeratedSetIsAFreshArrayAsTheDocumentationPromises()
        {
            using ZddManager manager = new ZddManager(4);

            int[][] sets = PowerSetOf(manager).ToArray();

            // バッファを使い回していれば、ToArray() した全要素が同じ配列を指すことになる。
            Assert.Equal(sets.Length, sets.Distinct(ReferenceEqualityComparer.Instance).Count());

            // 受け取った配列は呼び出し側のもの。書き換えても次の列挙には影響しない。
            foreach (int[] set in sets)
            {
                Array.Fill(set, -1);
            }

            Assert.Equal(new[] { 0, 1, 2, 3 }, PowerSetOf(manager).Last());
        }

        [Fact]
        public void EveryEnumeratedSetHasItsItemsInAscendingOrder()
        {
            const int VariableCount = 10;

            using ZddManager manager = new ZddManager(VariableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(VariableCount, 8, seed: 1420))
            {
                Zdd zdd = ZddFamilies.Build(manager, family);

                foreach (ZddEnumerationOrder order in new[] { ZddEnumerationOrder.Default, ZddEnumerationOrder.Lexicographic })
                {
                    foreach (int[] set in zdd.Sets(order))
                    {
                        Assert.Equal(set.OrderBy(item => item).ToArray(), set);
                    }
                }
            }
        }

        // ---- Contains ----

        [Fact]
        public void ContainsAgreesWithTheEnumerationOnEverySubsetUpToTwelveVariables()
        {
            for (int variableCount = 0; variableCount <= FamilyCases.ExhaustiveVariableLimit; variableCount++)
            {
                using ZddManager manager = new ZddManager(variableCount);

                foreach (BruteForceFamily family in Families(variableCount, seed: 1430 + variableCount))
                {
                    Zdd zdd = ZddFamilies.Build(manager, family);

                    // 列挙で出た集合はすべて属し、出なかった集合はすべて属さない。
                    foreach (int mask in FamilyCases.AllSubsets(variableCount))
                    {
                        Assert.Equal(family.Contains(mask), zdd.Contains(ItemsOf(mask)));
                    }
                }
            }
        }

        [Fact]
        public void ContainsAcceptsItemsInAnyOrderAndIgnoresRepeats()
        {
            using ZddManager manager = new ZddManager(6);

            Zdd family = ZddFamilies.Build(manager, new[] { 1, 4, 5 }, new[] { 0 });

            Assert.True(family.Contains(1, 4, 5));
            Assert.True(family.Contains(5, 1, 4));
            Assert.True(family.Contains(4, 4, 1, 5, 5, 5));

            // 部分集合でも上位集合でも「その集合そのもの」でなければ属さない。
            Assert.False(family.Contains(1, 4));
            Assert.False(family.Contains(1, 4, 5, 3));

            // IEnumerable<int> でも同じ答になる。
            Assert.True(family.Contains(new List<int> { 5, 4, 1 }));
            Assert.False(family.Contains(new List<int> { 2 }));
        }

        [Fact]
        public void ContainsWithNoItemsAsksWhetherTheFamilyHoldsTheEmptySet()
        {
            using ZddManager manager = new ZddManager(5);

            Assert.False(manager.Empty.Contains());
            Assert.True(manager.Base.Contains());
            Assert.False(manager.Singleton(2).Contains());
            Assert.True((manager.Singleton(2) | manager.Base).Contains());
            Assert.True(PowerSetOf(manager).Contains(Array.Empty<int>()));
        }

        [Fact]
        public void ContainsRejectsItemsOutsideTheUniverse()
        {
            using ZddManager manager = new ZddManager(4);

            Zdd family = PowerSetOf(manager);

            Assert.Throws<ArgumentOutOfRangeException>(() => family.Contains(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => family.Contains(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => family.Contains(new List<int> { 0, 9 }));
            Assert.Throws<ArgumentNullException>(() => family.Contains((IEnumerable<int>)null!));
        }

        // ---- IsSubsetOf / Overlaps ----

        [Fact]
        public void EveryPairOfThreeVariableFamiliesComparesLikeTheNaiveImplementation()
        {
            const int VariableCount = 3;

            using ZddManager manager = new ZddManager(VariableCount);

            BruteForceFamily[] families = FamilyCases.AllFamilies(VariableCount).ToArray();
            Zdd[] zdds = families.Select(family => ZddFamilies.Build(manager, family)).ToArray();

            for (int left = 0; left < families.Length; left++)
            {
                for (int right = 0; right < families.Length; right++)
                {
                    bool expectedSubset = families[left].Masks.All(mask => families[right].Contains(mask));
                    bool expectedOverlap = families[left].Masks.Any(mask => families[right].Contains(mask));

                    Assert.Equal(expectedSubset, zdds[left].IsSubsetOf(zdds[right]));
                    Assert.Equal(expectedOverlap, zdds[left].Overlaps(zdds[right]));
                }
            }
        }

        [Fact]
        public void ComparisonsMatchTheNaiveImplementationOnLargerFamilies()
        {
            const int VariableCount = 12;

            using ZddManager manager = new ZddManager(VariableCount);

            BruteForceFamily[] families = FamilyCases
                .RandomFamilies(VariableCount, 12, seed: 1440)
                .Concat(new[]
                {
                    BruteForceFamily.Empty(VariableCount),
                    BruteForceFamily.Base(VariableCount),
                    BruteForceFamily.PowerSet(VariableCount),
                })
                .ToArray();

            foreach (BruteForceFamily left in families)
            {
                foreach (BruteForceFamily right in families)
                {
                    Zdd f = ZddFamilies.Build(manager, left);
                    Zdd g = ZddFamilies.Build(manager, right);

                    // 族を作る版と作らない版が同じ答になること（短絡版は差や交わりを組み立てない）。
                    Assert.Equal((f - g).IsEmpty, f.IsSubsetOf(g));
                    Assert.Equal(!(f & g).IsEmpty, f.Overlaps(g));

                    Assert.Equal(left.Masks.All(mask => right.Contains(mask)), f.IsSubsetOf(g));
                    Assert.Equal(left.Masks.Any(mask => right.Contains(mask)), f.Overlaps(g));

                    // 交わりは対称、包含は自分自身について常に真。
                    Assert.Equal(f.Overlaps(g), g.Overlaps(f));
                    Assert.True(f.IsSubsetOf(f));
                }
            }
        }

        [Fact]
        public void TheEmptyFamilyIsBelowEveryFamilyAndOverlapsNone()
        {
            using ZddManager manager = new ZddManager(5);

            Zdd empty = manager.Empty;
            Zdd family = ZddFamilies.Build(manager, new[] { 0, 2 }, new[] { 4 });

            Assert.True(empty.IsSubsetOf(family));
            Assert.True(empty.IsSubsetOf(empty));
            Assert.False(family.IsSubsetOf(empty));

            Assert.False(empty.Overlaps(family));
            Assert.False(empty.Overlaps(empty));
            Assert.False(family.Overlaps(empty));

            // {∅} は「空集合を持つ族」とだけ交わる。
            Assert.False(family.Overlaps(manager.Base));
            Assert.True((family | manager.Base).Overlaps(manager.Base));
            Assert.True(manager.Base.Overlaps(PowerSetOf(manager)));
        }

        // ---- 誤用 ----

        [Fact]
        public void QueriesRejectHandlesFromAnotherManagerAndDefaultHandles()
        {
            using ZddManager manager = new ZddManager(4);
            using ZddManager other = new ZddManager(4);

            Zdd family = PowerSetOf(manager);
            Zdd stranger = PowerSetOf(other);
            Zdd invalid = default;

            Assert.Throws<ArgumentException>(() => family.IsSubsetOf(stranger));
            Assert.Throws<ArgumentException>(() => family.Overlaps(stranger));
            Assert.Throws<ArgumentException>(() => family.IsSubsetOf(invalid));
            Assert.Throws<ArgumentException>(() => family.Overlaps(invalid));

            Assert.Throws<InvalidOperationException>(() => invalid.Sets().GetEnumerator());
            Assert.Throws<InvalidOperationException>(() => invalid.Contains());
            Assert.Throws<InvalidOperationException>(() => invalid.IsSubsetOf(family));
            Assert.Throws<InvalidOperationException>(() => invalid.Overlaps(family));
        }

        [Fact]
        public void QueriesOnADisposedManagerThrowWhereTheyAreCalled()
        {
            ZddManager manager = new ZddManager(4);
            Zdd family = PowerSetOf(manager);
            Zdd other = manager.Singleton(1);
            manager.Dispose();

            // 列挙も、辿り始める前に（foreach の場所ではなく Sets() の場所で）弾く。
            Assert.Throws<ObjectDisposedException>(() => family.Sets());
            Assert.Throws<ObjectDisposedException>(() => family.Contains(1));
            Assert.Throws<ObjectDisposedException>(() => family.IsSubsetOf(other));
            Assert.Throws<ObjectDisposedException>(() => family.Overlaps(other));
        }

        [Fact]
        public void TheFamilyHandleIsEnumerableButNotACollection()
        {
            // 族の要素数は int に収まらないので ICollection は実装しない（docs/PLAN.md §8）。
            Assert.True(typeof(IEnumerable<int[]>).IsAssignableFrom(typeof(Zdd)));
            Assert.False(typeof(ICollection).IsAssignableFrom(typeof(Zdd)));
            Assert.False(typeof(ICollection<int[]>).IsAssignableFrom(typeof(Zdd)));
        }

        // ---- 深い ZDD（docs/PLAN.md §4.5 の回帰テスト）----

        [Fact]
        [Trait("Category", "Slow")]
        public void ADeepFamilyDoesNotOverflowTheStack()
        {
            using ZddManager manager = new ZddManager(DeepVariableCount);

            // 変数 10 万個すべてを含む集合 1 つだけの族。ノードが 10 万段に連なる。
            Zdd single = SingleFullSet(manager);
            Zdd powerSet = PowerSetOf(manager);

            // 列挙: 深さぶんの経路を降りても、明示スタックなので落ちない。
            int[][] sets = single.ToArray();
            Assert.Single(sets);
            Assert.Equal(DeepVariableCount, sets[0].Length);
            Assert.Equal(0, sets[0][0]);
            Assert.Equal(DeepVariableCount - 1, sets[0][^1]);

            Assert.Equal(sets[0], single.Sets(ZddEnumerationOrder.Lexicographic).Single());

            // 冪集合は列挙し切れないが、先頭だけなら取れる。
            Assert.Empty(powerSet.First());

            // メンバシップも 10 万段を降りる。
            Assert.True(single.Contains(sets[0]));
            Assert.False(single.Contains());
            Assert.True(powerSet.Contains(sets[0]));

            // 対を辿る側も同じく反復。
            Assert.True(single.IsSubsetOf(powerSet));
            Assert.False(powerSet.IsSubsetOf(single));
            Assert.True(single.Overlaps(powerSet));
        }

        // ---- 補助 ----

        /// <summary>列挙が素朴実装の族とぴったり一致すること（個数・重複の無さ・中身）。</summary>
        private static void AssertEnumerationMatchesNaive(ZddManager manager, BruteForceFamily expected)
        {
            Zdd zdd = ZddFamilies.Build(manager, expected);

            foreach (ZddEnumerationOrder order in new[] { ZddEnumerationOrder.Default, ZddEnumerationOrder.Lexicographic })
            {
                List<int> masks = new List<int>();
                foreach (int[] set in zdd.Sets(order))
                {
                    masks.Add(BruteForceFamily.MaskOf(expected.VariableCount, set));
                }

                // 重複が無いこと。族に落とすと重複が潰れてしまうので、先に個数で見る。
                Assert.Equal(masks.Count, masks.Distinct().Count());

                // 個数が Count（M1-12）と一致すること。
                Assert.Equal(new BigInteger(masks.Count), zdd.Count);

                // 集合として素朴実装の族と一致すること。
                Assert.Equal(expected, BruteForceFamily.FromMasks(expected.VariableCount, masks));
            }

            // foreach（GetEnumerator）は既定の順序で列挙する。
            Assert.Equal(
                zdd.Sets(ZddEnumerationOrder.Default).Select(Key).ToArray(),
                zdd.Select(Key).ToArray());
        }

        /// <summary>列挙の並びが <paramref name="comparison"/> の昇順になっていること。</summary>
        private static void AssertOrder(
            in Zdd zdd,
            BruteForceFamily expected,
            ZddEnumerationOrder order,
            Comparison<int> comparison)
        {
            int[] actual = zdd.Sets(order)
                .Select(set => BruteForceFamily.MaskOf(expected.VariableCount, set))
                .ToArray();

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
            int[] leftItems = ItemsOf(left);
            int[] rightItems = ItemsOf(right);

            for (int i = 0; i < Math.Min(leftItems.Length, rightItems.Length); i++)
            {
                if (leftItems[i] != rightItems[i])
                {
                    return leftItems[i] < rightItems[i] ? -1 : 1;
                }
            }

            return leftItems.Length.CompareTo(rightItems.Length);
        }

        /// <summary>ビットマスクを昇順の item 列に直す。</summary>
        private static int[] ItemsOf(int mask)
        {
            List<int> items = new List<int>();

            for (int item = 0; mask >> item != 0; item++)
            {
                if ((mask & (1 << item)) != 0)
                {
                    items.Add(item);
                }
            }

            return items.ToArray();
        }

        /// <summary>集合を並びごと比べられる文字列に直す（アサーションの読みやすさのため）。</summary>
        private static string Key(int[] set) => string.Join(",", set);

        /// <summary>照合に使う族の並び（境界 3 つ＋ランダム）。</summary>
        private static IEnumerable<BruteForceFamily> Families(int variableCount, int seed)
        {
            yield return BruteForceFamily.Empty(variableCount);
            yield return BruteForceFamily.Base(variableCount);
            yield return BruteForceFamily.PowerSet(variableCount);

            foreach (BruteForceFamily family in FamilyCases.RandomFamilies(variableCount, 4, seed))
            {
                yield return family;
            }
        }

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

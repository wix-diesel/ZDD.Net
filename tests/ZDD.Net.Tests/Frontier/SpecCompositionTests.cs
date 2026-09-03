using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Frontier;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// M3-5 completion criteria (issue #37) checked at the spec level, by brute-force exhaustive walk
    /// (<see cref="SpecWalker"/>): <c>spec1.And(spec2)</c> accepts exactly the intersection of what each
    /// accepts alone, <c>spec1.Or(spec2)</c> exactly the union, for every combination of
    /// <see cref="IDdSpec{TState}"/> and <see cref="IArrayDdSpec"/> operands, including a spec that
    /// actually skips levels (<see cref="EvenItemsOnlySpec"/>) — none of the library's built-in specs do,
    /// so without one the level-synchronization logic (<see cref="CompositionStep"/>) would go untested —
    /// and a composition three levels deep.
    /// </summary>
    public class SpecCompositionTests
    {
        [Theory]
        [InlineData(6, 1, 3)]
        [InlineData(6, 0, 6)]
        [InlineData(7, 2, 2)]
        [InlineData(5, 3, 5)]
        public void AndAcceptsExactlyTheIntersectionAcrossScalarSpecsWithASkippingOperand(int itemCount, int min, int max)
        {
            EvenItemsOnlySpec evens = new EvenItemsOnlySpec(itemCount);
            CardinalitySpec cardinality = new CardinalitySpec(itemCount, min, max);

            List<int[]> composed = SpecWalker.Accepted<AndSpec<EvenItemsOnlySpec, int, CardinalitySpec, int>, PairState<int, int>>(
                evens.And<EvenItemsOnlySpec, int, CardinalitySpec, int>(cardinality), itemCount);

            List<int[]> expected = Intersect(
                SpecWalker.Accepted<EvenItemsOnlySpec, int>(evens, itemCount),
                SpecWalker.Accepted<CardinalitySpec, int>(cardinality, itemCount));

            Assert.Equal(AsText(expected), AsText(composed));
        }

        [Theory]
        [InlineData(6, 1, 3)]
        [InlineData(6, 0, 6)]
        [InlineData(7, 2, 2)]
        [InlineData(5, 3, 5)]
        public void OrAcceptsExactlyTheUnionAcrossScalarSpecsWithASkippingOperand(int itemCount, int min, int max)
        {
            EvenItemsOnlySpec evens = new EvenItemsOnlySpec(itemCount);
            CardinalitySpec cardinality = new CardinalitySpec(itemCount, min, max);

            List<int[]> composed = SpecWalker.Accepted<OrSpec<EvenItemsOnlySpec, int, CardinalitySpec, int>, PairState<int, int>>(
                evens.Or<EvenItemsOnlySpec, int, CardinalitySpec, int>(cardinality), itemCount);

            List<int[]> expected = Union(
                SpecWalker.Accepted<EvenItemsOnlySpec, int>(evens, itemCount),
                SpecWalker.Accepted<CardinalitySpec, int>(cardinality, itemCount));

            Assert.Equal(AsText(expected), AsText(composed));
        }

        [Theory]
        [InlineData(5)]
        [InlineData(6)]
        public void AndAcceptsExactlyTheIntersectionForAnArrayOperandBridgedThroughTheAdapter(int itemCount)
        {
            AtMostOneSpec atMostOne = new AtMostOneSpec(itemCount);
            CardinalitySpec atLeastOne = new CardinalitySpec(itemCount, 1, itemCount);

            List<int[]> composed = SpecWalker.Accepted<
                AndSpec<ArrayDdSpecAdapter<AtMostOneSpec>, int[], CardinalitySpec, int>, PairState<int[], int>>(
                atMostOne.AsSpec().And<ArrayDdSpecAdapter<AtMostOneSpec>, int[], CardinalitySpec, int>(atLeastOne), itemCount);

            List<int[]> expected = Intersect(
                SpecWalker.Accepted<ArrayDdSpecAdapter<AtMostOneSpec>, int[]>(new ArrayDdSpecAdapter<AtMostOneSpec>(atMostOne), itemCount),
                SpecWalker.Accepted<CardinalitySpec, int>(atLeastOne, itemCount));

            // "At most one" ∩ "at least one" == exactly one.
            Assert.Equal(itemCount, composed.Count);
            Assert.All(composed, set => Assert.Single(set));
            Assert.Equal(AsText(expected), AsText(composed));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        public void OrAcceptsExactlyTheUnionAcrossTwoArrayOperands(int itemCount)
        {
            AtMostOneSpec atMostOne = new AtMostOneSpec(itemCount);
            FullSetOnlySpec fullSet = new FullSetOnlySpec(itemCount);

            List<int[]> composed = SpecWalker.Accepted<
                OrSpec<ArrayDdSpecAdapter<AtMostOneSpec>, int[], ArrayDdSpecAdapter<FullSetOnlySpec>, int[]>, PairState<int[], int[]>>(
                atMostOne.AsSpec().Or<ArrayDdSpecAdapter<AtMostOneSpec>, int[], ArrayDdSpecAdapter<FullSetOnlySpec>, int[]>(fullSet.AsSpec()), itemCount);

            List<int[]> expected = Union(
                SpecWalker.Accepted<ArrayDdSpecAdapter<AtMostOneSpec>, int[]>(new ArrayDdSpecAdapter<AtMostOneSpec>(atMostOne), itemCount),
                SpecWalker.Accepted<ArrayDdSpecAdapter<FullSetOnlySpec>, int[]>(new ArrayDdSpecAdapter<FullSetOnlySpec>(fullSet), itemCount));

            // itemCount > 1 keeps the full set genuinely outside "at most one", so the union is a strict superset of either side.
            Assert.True(expected.Count > SpecWalker.Accepted<ArrayDdSpecAdapter<AtMostOneSpec>, int[]>(new ArrayDdSpecAdapter<AtMostOneSpec>(atMostOne), itemCount).Count);
            Assert.Equal(AsText(expected), AsText(composed));
        }

        [Fact]
        public void ThreeLevelsOfCompositionTypeCheckAndComposeCorrectly()
        {
            const int itemCount = 6;
            EvenItemsOnlySpec evens = new EvenItemsOnlySpec(itemCount);
            CardinalitySpec atMostFour = new CardinalitySpec(itemCount, 0, 4);
            AtMostOneSpec atMostOne = new AtMostOneSpec(itemCount);

            // a.And(b).And(c): each .And returns a spec that is itself an IDdSpec, so this must type-check
            // without any special-casing, mixing a scalar⊗scalar composition further with an array operand.
            AndSpec<EvenItemsOnlySpec, int, CardinalitySpec, int> evensAndCardinality =
                evens.And<EvenItemsOnlySpec, int, CardinalitySpec, int>(atMostFour);
            var triple = evensAndCardinality.And<
                AndSpec<EvenItemsOnlySpec, int, CardinalitySpec, int>, PairState<int, int>, ArrayDdSpecAdapter<AtMostOneSpec>, int[]>(
                atMostOne.AsSpec());

            List<int[]> composed = SpecWalker.Accepted<
                AndSpec<AndSpec<EvenItemsOnlySpec, int, CardinalitySpec, int>, PairState<int, int>, ArrayDdSpecAdapter<AtMostOneSpec>, int[]>,
                PairState<PairState<int, int>, int[]>>(triple, itemCount);

            List<int[]> expected = Intersect(
                Intersect(
                    SpecWalker.Accepted<EvenItemsOnlySpec, int>(evens, itemCount),
                    SpecWalker.Accepted<CardinalitySpec, int>(atMostFour, itemCount)),
                SpecWalker.Accepted<ArrayDdSpecAdapter<AtMostOneSpec>, int[]>(new ArrayDdSpecAdapter<AtMostOneSpec>(atMostOne), itemCount));

            Assert.NotEmpty(expected);
            Assert.Equal(AsText(expected), AsText(composed));
        }

        private static List<int[]> Intersect(List<int[]> left, List<int[]> right)
        {
            HashSet<string> rightSet = right.Select(AsKey).ToHashSet();
            return left.Where(set => rightSet.Contains(AsKey(set))).ToList();
        }

        private static List<int[]> Union(List<int[]> left, List<int[]> right)
        {
            Dictionary<string, int[]> byKey = new Dictionary<string, int[]>();
            foreach (int[] set in left.Concat(right))
            {
                byKey[AsKey(set)] = set;
            }

            return byKey.Values.ToList();
        }

        private static string AsKey(int[] set) => string.Join(",", set);

        private static string AsText(IEnumerable<int[]> family) =>
            string.Join(" ", family.Select(set => "{" + string.Join(",", set) + "}").OrderBy(text => text, StringComparer.Ordinal));

        /// <summary>
        /// Accepts any subset of the even-indexed items; odd items are always excluded, and — unlike any
        /// built-in spec — are never even offered a real branch: <see cref="GetChild"/> jumps straight
        /// from one even item's level to the next, genuinely exercising the "skipped levels are excluded
        /// items" contract that <see cref="CompositionStep"/> has to keep in sync across two operands.
        /// </summary>
        private readonly struct EvenItemsOnlySpec : IDdSpec<int>
        {
            private readonly int _itemCount;

            public EvenItemsOnlySpec(int itemCount)
            {
                _itemCount = itemCount;
            }

            public int GetRoot(ref int state)
            {
                state = 0;
                return NextEvenLevel(0);
            }

            public int GetChild(ref int state, int level, int value)
            {
                int item = _itemCount - level;
                return NextEvenLevel(item + 1);
            }

            private int NextEvenLevel(int fromItem)
            {
                int item = (fromItem % 2 == 0) ? fromItem : fromItem + 1;
                return item >= _itemCount ? DdResult.True : _itemCount - item;
            }

            public bool StateEquals(in int left, in int right) => true;

            public int StateHashCode(in int state) => 0;
        }

        /// <summary>Accepts any subset containing at most one item — a minimal, easy-to-verify <see cref="IArrayDdSpec"/>.</summary>
        private readonly struct AtMostOneSpec : IArrayDdSpec
        {
            private readonly int _itemCount;

            public AtMostOneSpec(int itemCount)
            {
                _itemCount = itemCount;
            }

            public int ArrayLength => 1;

            public int GetRoot(Span<int> state)
            {
                state[0] = 0;
                return _itemCount == 0 ? DdResult.True : _itemCount;
            }

            public int GetChild(Span<int> state, int level, int value)
            {
                if (value == 1)
                {
                    if (state[0] == 1)
                    {
                        return DdResult.False;
                    }

                    state[0] = 1;
                }

                return level == 1 ? DdResult.True : level - 1;
            }
        }

        /// <summary>Accepts only the single set containing every item — the array-spec dual of "at most one".</summary>
        private readonly struct FullSetOnlySpec : IArrayDdSpec
        {
            private readonly int _itemCount;

            public FullSetOnlySpec(int itemCount)
            {
                _itemCount = itemCount;
            }

            public int ArrayLength => 0;

            public int GetRoot(Span<int> state) => _itemCount == 0 ? DdResult.True : _itemCount;

            public int GetChild(Span<int> state, int level, int value)
            {
                if (value == 0)
                {
                    return DdResult.False;
                }

                return level == 1 ? DdResult.True : level - 1;
            }
        }
    }
}

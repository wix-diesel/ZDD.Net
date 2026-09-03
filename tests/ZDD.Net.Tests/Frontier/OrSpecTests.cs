using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// M3-5 completion criteria for <see cref="OrSpec{TSpecA, TStateA, TSpecB, TStateB}"/>: a
    /// directly-built disjunction matches a post-filter (build each side, then <c>Union</c>) both when
    /// the two specs agree on every level and when their skip patterns disagree.
    /// </summary>
    public class OrSpecTests
    {
        [Theory]
        [InlineData(6, 0, 2, 4, 6)]
        [InlineData(7, 1, 3, 3, 5)]
        [InlineData(8, 2, 4, 2, 4)]
        public void MatchesUnionOfIndependentlyBuiltSpecs(int itemCount, int minA, int maxA, int minB, int maxB)
        {
            using ZddManager manager = new ZddManager(itemCount);

            CardinalitySpec specA = new CardinalitySpec(itemCount, minA, maxA);
            CardinalitySpec specB = new CardinalitySpec(itemCount, minB, maxB);

            Zdd direct = FrontierBuilder.Build<OrSpec<CardinalitySpec, int, CardinalitySpec, int>, OrState<int, int>>(
                manager, specA.Or<CardinalitySpec, int, CardinalitySpec, int>(specB));

            Zdd postFiltered = FrontierBuilder.Build<CardinalitySpec, int>(manager, specA)
                .Union(FrontierBuilder.Build<CardinalitySpec, int>(manager, specB));

            Assert.Equal(postFiltered, direct);
        }

        /// <summary>Same skip-mismatch spec as <c>AndSpecTests</c>, but here a broken skip degrades that side to "dead" instead of failing the whole branch.</summary>
        private readonly struct FirstAndLastFreeSpec : IDdSpec<int>
        {
            private readonly int _itemCount;

            public FirstAndLastFreeSpec(int itemCount) => _itemCount = itemCount;

            public int GetRoot(ref int state)
            {
                state = 0;
                return _itemCount <= 1 ? DdResult.True : _itemCount;
            }

            public int GetChild(ref int state, int level, int value) => level == 1 ? DdResult.True : 1;

            public bool StateEquals(in int left, in int right) => true;

            public int StateHashCode(in int state) => 0;
        }

        [Theory]
        [InlineData(5, 0, 1)]
        [InlineData(6, 4, 6)]
        [InlineData(7, 3, 3)]
        public void MatchesUnionWhenSubSpecsSkipDifferentLevels(int itemCount, int min, int max)
        {
            using ZddManager manager = new ZddManager(itemCount);

            FirstAndLastFreeSpec skipSpec = new FirstAndLastFreeSpec(itemCount);
            CardinalitySpec cardinality = new CardinalitySpec(itemCount, min, max);

            Zdd direct = FrontierBuilder.Build<
                OrSpec<FirstAndLastFreeSpec, int, CardinalitySpec, int>,
                OrState<int, int>>(
                manager, skipSpec.Or<FirstAndLastFreeSpec, int, CardinalitySpec, int>(cardinality));

            Zdd postFiltered = FrontierBuilder.Build<FirstAndLastFreeSpec, int>(manager, skipSpec)
                .Union(FrontierBuilder.Build<CardinalitySpec, int>(manager, cardinality));

            Assert.Equal(postFiltered, direct);
        }

        [Fact]
        public void ThreeSpecsComposeOrAndMatchTripleUnion()
        {
            const int itemCount = 8;
            using ZddManager manager = new ZddManager(itemCount);

            CardinalitySpec specA = new CardinalitySpec(itemCount, 0, 1);
            CardinalitySpec specB = new CardinalitySpec(itemCount, 3, 4);
            CardinalitySpec specC = new CardinalitySpec(itemCount, 7, 8);

            OrSpec<CardinalitySpec, int, CardinalitySpec, int> ab =
                specA.Or<CardinalitySpec, int, CardinalitySpec, int>(specB);

            OrSpec<OrSpec<CardinalitySpec, int, CardinalitySpec, int>, OrState<int, int>, CardinalitySpec, int> abc =
                ab.Or<OrSpec<CardinalitySpec, int, CardinalitySpec, int>, OrState<int, int>, CardinalitySpec, int>(specC);

            Zdd direct = FrontierBuilder.Build<
                OrSpec<OrSpec<CardinalitySpec, int, CardinalitySpec, int>, OrState<int, int>, CardinalitySpec, int>,
                OrState<OrState<int, int>, int>>(manager, abc);

            Zdd postFiltered = FrontierBuilder.Build<CardinalitySpec, int>(manager, specA)
                .Union(FrontierBuilder.Build<CardinalitySpec, int>(manager, specB))
                .Union(FrontierBuilder.Build<CardinalitySpec, int>(manager, specC));

            Assert.Equal(postFiltered, direct);
        }

        [Theory]
        [InlineData(6, 0, 2, 5, 6)]
        [InlineData(7, 0, 0, 2, 4)]
        public void SpecWalkerAcceptsExactlyEitherSidesUnion(int itemCount, int minA, int maxA, int minB, int maxB)
        {
            CardinalitySpec specA = new CardinalitySpec(itemCount, minA, maxA);
            CardinalitySpec specB = new CardinalitySpec(itemCount, minB, maxB);

            List<int[]> acceptedA = SpecWalker.Accepted<CardinalitySpec, int>(specA, itemCount);
            List<int[]> acceptedB = SpecWalker.Accepted<CardinalitySpec, int>(specB, itemCount);
            HashSet<string> expected = new HashSet<string>(acceptedA.Select(AsKey).Union(acceptedB.Select(AsKey)));

            OrSpec<CardinalitySpec, int, CardinalitySpec, int> combined =
                specA.Or<CardinalitySpec, int, CardinalitySpec, int>(specB);
            List<int[]> acceptedCombined =
                SpecWalker.Accepted<OrSpec<CardinalitySpec, int, CardinalitySpec, int>, OrState<int, int>>(
                    combined, itemCount);

            Assert.Equal(expected, new HashSet<string>(acceptedCombined.Select(AsKey)));
        }

        private static string AsKey(int[] set) => string.Join(",", set);
    }
}

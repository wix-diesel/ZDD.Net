using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// M3-5 completion criteria for <see cref="AndSpec{TSpecA, TStateA, TSpecB, TStateB}"/>: a
    /// directly-built conjunction matches a post-filter (build each side, then <c>Intersect</c>) for
    /// specs that agree on every level and for specs whose skip patterns disagree (the "level
    /// synchronization" the issue calls out by name), for the "simple path AND edge count" example the
    /// issue gives verbatim, and three levels deep.
    /// </summary>
    public class AndSpecTests
    {
        [Theory]
        [InlineData(6, 1, 4, 2, 5)]
        [InlineData(6, 0, 6, 3, 3)]
        [InlineData(8, 2, 5, 4, 8)]
        public void MatchesIntersectOfIndependentlyBuiltSpecs(int itemCount, int minA, int maxA, int minB, int maxB)
        {
            using ZddManager manager = new ZddManager(itemCount);

            CardinalitySpec specA = new CardinalitySpec(itemCount, minA, maxA);
            CardinalitySpec specB = new CardinalitySpec(itemCount, minB, maxB);

            Zdd direct = FrontierBuilder.Build<AndSpec<CardinalitySpec, int, CardinalitySpec, int>, AndState<int, int>>(
                manager, specA.And<CardinalitySpec, int, CardinalitySpec, int>(specB));

            Zdd postFiltered = FrontierBuilder.Build<CardinalitySpec, int>(manager, specA)
                .Intersect(FrontierBuilder.Build<CardinalitySpec, int>(manager, specB));

            Assert.Equal(postFiltered, direct);
        }

        /// <summary>
        /// A spec whose skip pattern disagrees with everything else: only item 0 and the last item can
        /// ever be included; every item in between is skipped straight past (forced excluded) rather
        /// than visited one at a time. Composed with a spec that visits every level, this exercises the
        /// "what if the two specs' next decision levels differ" rule directly (docs/PLAN.md §6.3).
        /// </summary>
        private readonly struct FirstAndLastFreeSpec : IDdSpec<int>
        {
            private readonly int _itemCount;

            public FirstAndLastFreeSpec(int itemCount) => _itemCount = itemCount;

            public int GetRoot(ref int state)
            {
                state = 0;
                return _itemCount <= 1 ? DdResult.True : _itemCount;
            }

            public int GetChild(ref int state, int level, int value)
            {
                // Item 0's branch (either value) skips straight to level 1 (the last item), forcing
                // every item in between to be excluded without visiting them.
                // The last item's branch (either value) accepts unconditionally.
                return level == 1 ? DdResult.True : 1;
            }

            public bool StateEquals(in int left, in int right) => true;

            public int StateHashCode(in int state) => 0;
        }

        [Theory]
        [InlineData(5, 0, 2)]
        [InlineData(5, 1, 1)]
        [InlineData(6, 0, 0)]
        [InlineData(6, 2, 6)]
        public void MatchesIntersectWhenSubSpecsSkipDifferentLevels(int itemCount, int min, int max)
        {
            using ZddManager manager = new ZddManager(itemCount);

            FirstAndLastFreeSpec skipSpec = new FirstAndLastFreeSpec(itemCount);
            CardinalitySpec cardinality = new CardinalitySpec(itemCount, min, max);

            Zdd direct = FrontierBuilder.Build<
                AndSpec<FirstAndLastFreeSpec, int, CardinalitySpec, int>,
                AndState<int, int>>(
                manager, skipSpec.And<FirstAndLastFreeSpec, int, CardinalitySpec, int>(cardinality));

            Zdd postFiltered = FrontierBuilder.Build<FirstAndLastFreeSpec, int>(manager, skipSpec)
                .Intersect(FrontierBuilder.Build<CardinalitySpec, int>(manager, cardinality));

            Assert.Equal(postFiltered, direct);

            // The independent characterization: only item 0 and the last item may ever be chosen.
            foreach (int[] set in direct.Sets())
            {
                Assert.True(set.All(item => item == 0 || item == itemCount - 1));
            }
        }

        /// <summary>
        /// The issue's own example, verbatim: "a simple s-t path AND at most k edges", built directly
        /// rather than as a post-filter over an intermediate path family that can be dramatically
        /// larger than the filtered result.
        /// </summary>
        [Theory]
        [InlineData(4, 6)]
        [InlineData(4, 8)]
        [InlineData(5, 10)]
        public void SimplePathAndEdgeCountMatchesPostFilterIntersection(int gridSize, int maxEdges)
        {
            Graph grid = Graph.Grid(gridSize, gridSize);
            int s = 0;
            int t = grid.VertexCount - 1;
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            PathSpec pathSpec = new PathSpec(grid, s, t);
            CardinalitySpec atMostKEdges = new CardinalitySpec(grid.EdgeCount, 0, maxEdges);

            ArrayDdSpecAdapter<PathSpec> pathAsSpec = pathSpec.AsDdSpec();

            Zdd direct = FrontierBuilder.Build<
                AndSpec<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>,
                AndState<int[], int>>(
                manager,
                pathAsSpec.And<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>(atMostKEdges));

            Zdd postFiltered = FrontierBuilder.Build<PathSpec>(manager, pathSpec)
                .Intersect(FrontierBuilder.Build<CardinalitySpec, int>(manager, atMostKEdges));

            Assert.Equal(postFiltered, direct);
        }

        /// <summary>Three levels of <c>.And</c> compose as a type, and give the three-way intersection.</summary>
        [Fact]
        public void ThreeSpecsComposeAndMatchTripleIntersect()
        {
            const int itemCount = 8;
            using ZddManager manager = new ZddManager(itemCount);

            CardinalitySpec specA = new CardinalitySpec(itemCount, 1, 7);
            CardinalitySpec specB = new CardinalitySpec(itemCount, 2, 6);
            CardinalitySpec specC = new CardinalitySpec(itemCount, 0, 4);

            AndSpec<CardinalitySpec, int, CardinalitySpec, int> ab =
                specA.And<CardinalitySpec, int, CardinalitySpec, int>(specB);

            AndSpec<AndSpec<CardinalitySpec, int, CardinalitySpec, int>, AndState<int, int>, CardinalitySpec, int> abc =
                ab.And<AndSpec<CardinalitySpec, int, CardinalitySpec, int>, AndState<int, int>, CardinalitySpec, int>(specC);

            Zdd direct = FrontierBuilder.Build<
                AndSpec<AndSpec<CardinalitySpec, int, CardinalitySpec, int>, AndState<int, int>, CardinalitySpec, int>,
                AndState<AndState<int, int>, int>>(manager, abc);

            Zdd postFiltered = FrontierBuilder.Build<CardinalitySpec, int>(manager, specA)
                .Intersect(FrontierBuilder.Build<CardinalitySpec, int>(manager, specB))
                .Intersect(FrontierBuilder.Build<CardinalitySpec, int>(manager, specC));

            Assert.Equal(postFiltered, direct);
        }

        /// <summary>
        /// Small-scale, brute-force-style contract check via <see cref="SpecWalker"/>: the composed
        /// spec's own <c>GetRoot</c>/<c>GetChild</c> satisfy the interface's level and hash contracts,
        /// and the accepted family is the set intersection of what each side alone accepts.
        /// </summary>
        [Theory]
        [InlineData(6, 2, 4, 1, 5)]
        [InlineData(7, 0, 3, 3, 7)]
        public void SpecWalkerAcceptsExactlyBothSidesIntersection(int itemCount, int minA, int maxA, int minB, int maxB)
        {
            CardinalitySpec specA = new CardinalitySpec(itemCount, minA, maxA);
            CardinalitySpec specB = new CardinalitySpec(itemCount, minB, maxB);

            List<int[]> acceptedA = SpecWalker.Accepted<CardinalitySpec, int>(specA, itemCount);
            List<int[]> acceptedB = SpecWalker.Accepted<CardinalitySpec, int>(specB, itemCount);
            HashSet<string> expected = new HashSet<string>(
                acceptedA.Select(AsKey).Intersect(acceptedB.Select(AsKey)));

            AndSpec<CardinalitySpec, int, CardinalitySpec, int> combined =
                specA.And<CardinalitySpec, int, CardinalitySpec, int>(specB);
            List<int[]> acceptedCombined =
                SpecWalker.Accepted<AndSpec<CardinalitySpec, int, CardinalitySpec, int>, AndState<int, int>>(
                    combined, itemCount);

            Assert.Equal(expected, new HashSet<string>(acceptedCombined.Select(AsKey)));
        }

        private static string AsKey(int[] set) => string.Join(",", set);
    }
}

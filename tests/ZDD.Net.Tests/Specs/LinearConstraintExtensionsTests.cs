using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M6-8 completion criteria for <see cref="LinearConstraintExtensions"/>' cost filters
    /// (Graphillion's <c>cost_le</c>, translated to .NET naming): <see cref="Zdd.CostAtMost"/> /
    /// <c>CostAtLeast</c> / <c>CostEquals</c> match a post-hoc filter of an existing family for all
    /// three operators (variables &#8804; 12), agree exactly with the M3-5 primitive
    /// <c>zdd.Subset(new LinearConstraintSpec(...))</c> they wrap, hold at negative coefficients and
    /// boundary bounds (the exact minimum/maximum achievable cost), and build a smaller intermediate
    /// diagram than materializing the base family in full before filtering would.
    /// </summary>
    public class LinearConstraintExtensionsTests
    {
        [Theory]
        [InlineData(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, 30L)]
        [InlineData(new long[] { -6, 5, -4, 3, -2, 1, -1, 2, -3, 4, -5, 6 }, 0L)]
        [InlineData(new long[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 }, 12L)]
        public void CostAtMostMatchesAPostHocFilterOfAnExistingFamily(long[] costs, long bound) =>
            AssertMatchesPostHocFilter(costs, LinearConstraintOperator.LessOrEqual, bound);

        [Theory]
        [InlineData(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, 30L)]
        [InlineData(new long[] { -6, 5, -4, 3, -2, 1, -1, 2, -3, 4, -5, 6 }, 0L)]
        [InlineData(new long[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 }, 12L)]
        public void CostAtLeastMatchesAPostHocFilterOfAnExistingFamily(long[] costs, long bound) =>
            AssertMatchesPostHocFilter(costs, LinearConstraintOperator.GreaterOrEqual, bound);

        [Theory]
        [InlineData(new long[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, 30L)]
        [InlineData(new long[] { -6, 5, -4, 3, -2, 1, -1, 2, -3, 4, -5, 6 }, 0L)]
        [InlineData(new long[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 }, 12L)]
        public void CostEqualsMatchesAPostHocFilterOfAnExistingFamily(long[] costs, long bound) =>
            AssertMatchesPostHocFilter(costs, LinearConstraintOperator.Equal, bound);

        [Fact]
        public void BoundaryBoundsAtTheExactMinimumAndMaximumAchievableCostKeepOnlyThatOneSet()
        {
            long[] costs = { -3, 2, -1, 4, -5, 6 };
            using ZddManager manager = new ZddManager(costs.Length);
            Zdd everything = FrontierBuilder.Build<PowerSetSpec, byte>(manager, new PowerSetSpec(costs.Length));

            long minCost = costs.Where(c => c < 0).Sum();
            long maxCost = costs.Where(c => c > 0).Sum();

            int minMask = 0, maxMask = 0;
            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i] < 0)
                {
                    minMask |= 1 << i;
                }
                else if (costs[i] > 0)
                {
                    maxMask |= 1 << i;
                }
            }

            Zdd atMinCostAtMost = everything.CostAtMost(costs, minCost);
            Zdd atMinCostEquals = everything.CostEquals(costs, minCost);
            Zdd atMaxCostAtLeast = everything.CostAtLeast(costs, maxCost);
            Zdd atMaxCostEquals = everything.CostEquals(costs, maxCost);

            BruteForceFamily expectedMin = BruteForceFamily.FromMasks(costs.Length, new[] { minMask });
            BruteForceFamily expectedMax = BruteForceFamily.FromMasks(costs.Length, new[] { maxMask });

            // No set costs less than minCost / more than maxCost, so "at most the minimum" and "at
            // least the maximum" narrow down to exactly the same single set as "equals" that bound.
            FamilyAssert.AssertSameFamily(atMinCostAtMost, expectedMin);
            FamilyAssert.AssertSameFamily(atMinCostEquals, expectedMin);
            FamilyAssert.AssertSameFamily(atMaxCostAtLeast, expectedMax);
            FamilyAssert.AssertSameFamily(atMaxCostEquals, expectedMax);
        }

        [Fact]
        public void ConstructionTimeCostFilterBuildsFewerNodesThanBuildingTheCostFilterUnconstrained()
        {
            // A base family (grid s-t paths) that is itself already a narrow slice of the full power
            // set: most edge combinations are not simple paths at all. Folding the cost bound into the
            // same frontier walk (Subset, which CostAtMost wraps) only ever explores sums reachable by
            // an actual path, whereas building the cost filter on its own has to account for every
            // reachable sum over the full item universe, regardless of whether any base member reaches it.
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);

            long[] costs = new long[grid.EdgeCount];
            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = i % 2 == 0 ? i + 1 : -(i + 1);
            }

            const long bound = 3;
            LinearConstraintSpec costFilter = new LinearConstraintSpec(costs, LinearConstraintOperator.LessOrEqual, bound);
            ZddSpec baseSpec = new ZddSpec(basePaths.Zdd);
            AndSpec<ZddSpec, int, LinearConstraintSpec, long> combined =
                new AndSpec<ZddSpec, int, LinearConstraintSpec, long>(baseSpec, costFilter);

            long constructionTimeNodeCount = TopDownExpander<AndSpec<ZddSpec, int, LinearConstraintSpec, long>, AndState<int, long>>
                .Expand(combined).NodeCount;
            long costFilterAloneNodeCount = TopDownExpander<LinearConstraintSpec, long>.Expand(costFilter).NodeCount;

            Assert.True(
                constructionTimeNodeCount < costFilterAloneNodeCount,
                $"expected the fused cost filter ({constructionTimeNodeCount} nodes, restricted to sums a " +
                $"real path can reach) to build fewer nodes than the cost filter built unconstrained " +
                $"({costFilterAloneNodeCount} nodes, over the full item universe).");
        }

        [Fact]
        public void LongCoefficientConstructorAgreesWithTheIntCoefficientConstructor()
        {
            int[] intCoefficients = { 2, -3, 5, 7, -1, 4, -6, 3, 1, -2 };
            long[] longCoefficients = Array.ConvertAll(intCoefficients, c => (long)c);
            const long bound = 4;

            using ZddManager manager = new ZddManager(intCoefficients.Length);

            Zdd viaInt = FrontierBuilder.Build<LinearConstraintSpec, long>(
                manager, new LinearConstraintSpec(intCoefficients, LinearConstraintOperator.LessOrEqual, bound));
            Zdd viaLong = FrontierBuilder.Build<LinearConstraintSpec, long>(
                manager, new LinearConstraintSpec(longCoefficients, LinearConstraintOperator.LessOrEqual, bound));

            Assert.Equal(viaInt, viaLong);
        }

        private static void AssertMatchesPostHocFilter(long[] costs, LinearConstraintOperator op, long bound)
        {
            int itemCount = costs.Length;
            using ZddManager manager = new ZddManager(itemCount);

            // A non-trivial existing family (not the full power set) to filter, matching the
            // completion criterion's "an existing family" — not building from scratch under the bound.
            Zdd baseFamily = FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(itemCount, 1, itemCount - 1));

            Zdd filtered = op switch
            {
                LinearConstraintOperator.LessOrEqual => baseFamily.CostAtMost(costs, bound),
                LinearConstraintOperator.GreaterOrEqual => baseFamily.CostAtLeast(costs, bound),
                LinearConstraintOperator.Equal => baseFamily.CostEquals(costs, bound),
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };

            // Post-hoc: enumerate the base family and keep only the sets whose total cost satisfies the operator.
            List<int> keptMasks = new List<int>();
            foreach (int[] set in baseFamily.Sets())
            {
                long sum = 0;
                foreach (int item in set)
                {
                    sum += costs[item];
                }

                if (Accepts(op, sum, bound))
                {
                    keptMasks.Add(BruteForceFamily.MaskOf(itemCount, set));
                }
            }

            BruteForceFamily expected = BruteForceFamily.FromMasks(itemCount, keptMasks);
            FamilyAssert.AssertSameFamily(filtered, expected);

            // Agrees exactly with the M3-5 primitive this is a thin wrapper over.
            Zdd viaSubset = baseFamily.Subset<LinearConstraintSpec, long>(new LinearConstraintSpec(costs, op, bound));
            Assert.Equal(viaSubset, filtered);
        }

        private static bool Accepts(LinearConstraintOperator op, long sum, long bound) => op switch
        {
            LinearConstraintOperator.LessOrEqual => sum <= bound,
            LinearConstraintOperator.GreaterOrEqual => sum >= bound,
            LinearConstraintOperator.Equal => sum == bound,
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
    }
}

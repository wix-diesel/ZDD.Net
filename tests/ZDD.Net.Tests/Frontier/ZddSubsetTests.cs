using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// M3-5 completion criteria for <see cref="ZddExtensions.Subset{TSpec, TState}"/> (TdZdd's
    /// <c>zddSubset</c>): filtering an already-built <see cref="Zdd"/> by a spec gives the same result
    /// as building that spec on its own and <c>Intersect</c>-ing it in.
    /// </summary>
    public class ZddSubsetTests
    {
        [Theory]
        [InlineData(6, 1, 4)]
        [InlineData(6, 0, 6)]
        [InlineData(8, 3, 5)]
        public void MatchesIntersectWithTheSpecBuiltOnItsOwn(int itemCount, int min, int max)
        {
            using ZddManager manager = new ZddManager(itemCount);

            Zdd baseline = FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(itemCount, 2, itemCount - 1));
            CardinalitySpec filter = new CardinalitySpec(itemCount, min, max);

            Zdd subset = baseline.Subset<CardinalitySpec, int>(filter);
            Zdd expected = baseline.Intersect(FrontierBuilder.Build<CardinalitySpec, int>(manager, filter));

            Assert.Equal(expected, subset);
        }

        [Fact]
        public void FiltersAGridPathFamilyByEdgeCount()
        {
            Graph grid = Graph.Grid(4, 4);
            int s = 0;
            int t = grid.VertexCount - 1;
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            Zdd paths = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, s, t));
            CardinalitySpec atMostEight = new CardinalitySpec(grid.EdgeCount, 0, 8);

            Zdd subset = paths.Subset<CardinalitySpec, int>(atMostEight);
            Zdd expected = paths.Intersect(FrontierBuilder.Build<CardinalitySpec, int>(manager, atMostEight));

            Assert.Equal(expected, subset);
            Assert.True(subset.Count > 0);
            Assert.True(subset.Count < paths.Count);
        }

        [Fact]
        public void SubsetOfThePowerSetIsTheFilterItself()
        {
            const int itemCount = 6;
            using ZddManager manager = new ZddManager(itemCount);

            Zdd everything = FrontierBuilder.Build<PowerSetSpec, byte>(manager, new PowerSetSpec(itemCount));
            CardinalitySpec filter = new CardinalitySpec(itemCount, 2, 4);

            Zdd subset = everything.Subset<CardinalitySpec, int>(filter);
            Zdd expected = FrontierBuilder.Build<CardinalitySpec, int>(manager, filter);

            Assert.Equal(expected, subset);
        }
    }
}

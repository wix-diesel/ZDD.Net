using System.Numerics;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Frontier
{
    /// <summary>
    /// M3-5 completion criteria (issue #37) at the <see cref="FrontierBuilder"/> level, on real graph
    /// specs: since a <see cref="Zdd"/> is canonical, two families built in the same
    /// <see cref="ZddManager"/> are the exact same handle iff they represent the same family — so
    /// asserting <see cref="Zdd.Equals(Zdd)"/> here is a stronger check than comparing sets.
    /// </summary>
    public class SpecCompositionBuildTests
    {
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        public void AndOnPathAndCardinalityMatchesBuildingEachThenIntersecting(int n)
        {
            Graph grid = Graph.Grid(n, n);
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            PathSpec pathSpec = new PathSpec(grid, 0, grid.VertexCount - 1);
            CardinalitySpec shortEnough = new CardinalitySpec(grid.EdgeCount, 0, grid.EdgeCount / 2);

            Zdd direct = FrontierBuilder.Build<
                AndSpec<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>, PairState<int[], int>>(
                manager, pathSpec.AsSpec().And<ArrayDdSpecAdapter<PathSpec>, int[], CardinalitySpec, int>(shortEnough));

            Zdd paths = FrontierBuilder.Build<PathSpec>(manager, pathSpec);
            Zdd cardinalityOnly = FrontierBuilder.Build<CardinalitySpec, int>(manager, shortEnough);
            Zdd postFilter = paths.Intersect(cardinalityOnly);

            Assert.Equal(postFilter, direct);
            Assert.NotEqual(BigInteger.Zero, direct.Count);
            // Sanity: the cardinality bound must actually exclude some paths, or this proves nothing.
            Assert.True(direct.Count < paths.Count);
        }

        [Fact]
        public void OrOnTwoPathSpecsMatchesBuildingEachThenUnioning()
        {
            Graph grid = Graph.Grid(4, 4);
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            PathSpec anyEndpoints = new PathSpec(grid, 0, 0, allowAnyEndpoints: true);
            PathSpec cornerToCorner = new PathSpec(grid, 0, grid.VertexCount - 1);

            Zdd direct = FrontierBuilder.Build<
                OrSpec<ArrayDdSpecAdapter<PathSpec>, int[], ArrayDdSpecAdapter<PathSpec>, int[]>, PairState<int[], int[]>>(
                manager,
                anyEndpoints.AsSpec().Or<ArrayDdSpecAdapter<PathSpec>, int[], ArrayDdSpecAdapter<PathSpec>, int[]>(cornerToCorner.AsSpec()));

            Zdd anyBuilt = FrontierBuilder.Build<PathSpec>(manager, anyEndpoints);
            Zdd cornerBuilt = FrontierBuilder.Build<PathSpec>(manager, cornerToCorner);
            Zdd postFilter = anyBuilt.Union(cornerBuilt);

            Assert.Equal(postFilter, direct);
            // Every corner-to-corner path is already a path with some pair of endpoints, so the union
            // adds nothing over anyEndpoints alone — a useful cross-check on Or's own correctness.
            Assert.Equal(anyBuilt, direct);
        }

        [Fact]
        public void SubsetMatchesBuildingTheSpecThenIntersecting()
        {
            Graph grid = Graph.Grid(4, 4);
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            PathSpec pathSpec = new PathSpec(grid, 0, grid.VertexCount - 1);
            Zdd paths = FrontierBuilder.Build<PathSpec>(manager, pathSpec);

            CardinalitySpec shortEnough = new CardinalitySpec(grid.EdgeCount, 0, grid.EdgeCount / 2);
            Zdd viaSubset = paths.Subset<CardinalitySpec, int>(shortEnough);

            Zdd cardinalityOnly = FrontierBuilder.Build<CardinalitySpec, int>(manager, shortEnough);
            Zdd postFilter = paths.Intersect(cardinalityOnly);

            Assert.Equal(postFilter, viaSubset);
            Assert.True(viaSubset.Count < paths.Count);
        }

        [Fact]
        public void SubsetOfTheEmptyFamilyIsEmpty()
        {
            Graph grid = Graph.Grid(3, 3);
            using ZddManager manager = new ZddManager(grid.EdgeCount);

            // s == t: PathSpec is unsatisfiable, so this is Empty (ZddManager.Empty), the family ∅.
            PathSpec impossible = new PathSpec(grid, 0, 0);
            Zdd empty = FrontierBuilder.Build<PathSpec>(manager, impossible);
            Assert.True(empty.IsEmpty);

            CardinalitySpec anySize = new CardinalitySpec(grid.EdgeCount, 0, grid.EdgeCount);
            Zdd stillEmpty = empty.Subset<CardinalitySpec, int>(anySize);

            Assert.True(stillEmpty.IsEmpty);
        }
    }
}

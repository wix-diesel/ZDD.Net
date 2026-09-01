using System;
using System.Linq;
using Xunit;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M2-7 completion criteria for <see cref="FrontierManager"/>: introduced/forgotten vertices, frontier
    /// size and <see cref="FrontierManager.MaxFrontierSize"/> against hand-computed values for small graphs,
    /// mate-slot reuse and uniqueness, edge-order sensitivity, disconnected/isolated-vertex graphs, and
    /// linear-time construction.
    /// </summary>
    public class FrontierManagerTests
    {
        // Path(4): edges (0,1) (1,2) (2,3). Intervals [first,last]: v0=[0,0] v1=[0,1] v2=[1,2] v3=[2,2].
        [Fact]
        public void PathFourMatchesHandComputedFrontier()
        {
            var manager = new FrontierManager(Graph.Path(4));

            Assert.Equal(new[] { 0, 1 }, manager.IntroducedVertices(0));
            Assert.Equal(new[] { 2 }, manager.IntroducedVertices(1));
            Assert.Equal(new[] { 3 }, manager.IntroducedVertices(2));

            Assert.Equal(new[] { 0 }, manager.ForgottenVertices(0));
            Assert.Equal(new[] { 1 }, manager.ForgottenVertices(1));
            Assert.Equal(new[] { 2, 3 }, manager.ForgottenVertices(2));

            Assert.Equal(new[] { 2, 2, 2 }, new[] { manager.FrontierSize(0), manager.FrontierSize(1), manager.FrontierSize(2) });
            Assert.Equal(2, manager.MaxFrontierSize);
        }

        // Cycle(4): edges (0,1) (1,2) (2,3) (3,0). Intervals: v0=[0,3] v1=[0,1] v2=[1,2] v3=[2,3].
        [Fact]
        public void CycleFourMatchesHandComputedFrontier()
        {
            var manager = new FrontierManager(Graph.Cycle(4));

            Assert.Equal(new[] { 0, 1 }, manager.IntroducedVertices(0));
            Assert.Equal(Array.Empty<int>(), manager.ForgottenVertices(0));

            Assert.Equal(new[] { 2 }, manager.IntroducedVertices(1));
            Assert.Equal(new[] { 1 }, manager.ForgottenVertices(1));

            Assert.Equal(new[] { 3 }, manager.IntroducedVertices(2));
            Assert.Equal(new[] { 2 }, manager.ForgottenVertices(2));

            Assert.Equal(Array.Empty<int>(), manager.IntroducedVertices(3));
            Assert.Equal(new[] { 0, 3 }, manager.ForgottenVertices(3));

            Assert.Equal(new[] { 2, 3, 3, 2 }, Enumerable.Range(0, 4).Select(manager.FrontierSize).ToArray());
            Assert.Equal(3, manager.MaxFrontierSize);
        }

        // Grid(2,2): edges (0,1) (0,2) (1,3) (2,3). Intervals: v0=[0,1] v1=[0,2] v2=[1,3] v3=[2,3].
        [Fact]
        public void Grid2x2MatchesHandComputedFrontier()
        {
            var manager = new FrontierManager(Graph.Grid(2, 2));

            Assert.Equal(new[] { 2, 3, 3, 2 }, Enumerable.Range(0, 4).Select(manager.FrontierSize).ToArray());
            Assert.Equal(3, manager.MaxFrontierSize);
        }

        // Grid(3,3): 12 edges; intervals derived from Graph.Grid's row-by-row edge order (see class remarks).
        [Fact]
        public void Grid3x3MatchesHandComputedFrontier()
        {
            var manager = new FrontierManager(Graph.Grid(3, 3));

            var expected = new[] { 2, 3, 4, 4, 4, 3, 3, 4, 4, 4, 3, 2 };
            Assert.Equal(expected, Enumerable.Range(0, 12).Select(manager.FrontierSize).ToArray());
            Assert.Equal(4, manager.MaxFrontierSize);
        }

        [Fact]
        public void MateSlotsAreReusedAndNeverExceedMaxFrontierSize()
        {
            var manager = new FrontierManager(Graph.Path(4));

            // v0 and v2 leave/enter at edge 0/1 boundary and should share a slot; likewise v1/v3.
            Assert.Equal(manager.MateIndex(0, 0), manager.MateIndex(1, 2));
            Assert.Equal(manager.MateIndex(0, 1), manager.MateIndex(2, 3));

            var graph = Graph.Grid(3, 3);
            var grid = new FrontierManager(graph);
            int maxSlotSeen = -1;
            var current = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                foreach (int v in grid.IntroducedVertices(i))
                {
                    current.Add(v);
                }

                var slotsAtThisEdge = new System.Collections.Generic.HashSet<int>();
                foreach (int v in current)
                {
                    int slot = grid.MateIndex(i, v);
                    Assert.True(slotsAtThisEdge.Add(slot), $"Slot {slot} used by more than one vertex at edge {i}.");
                    maxSlotSeen = Math.Max(maxSlotSeen, slot);
                }

                Assert.Equal(current.Count, grid.FrontierSize(i));

                foreach (int v in grid.ForgottenVertices(i))
                {
                    current.Remove(v);
                }
            }

            Assert.True(maxSlotSeen < grid.MaxFrontierSize, "Slot count must not exceed MaxFrontierSize.");
        }

        [Fact]
        public void MateIndexThrowsForAVertexNotInTheFrontierAtThatEdge()
        {
            var manager = new FrontierManager(Graph.Path(4));

            // v3 only appears at edge 2 (its first and last incident edge), so it isn't in the frontier at edge 0.
            Assert.Throws<ArgumentException>(() => manager.MateIndex(0, 3));
        }

        [Fact]
        public void ReorderingEdgesChangesMaxFrontierSize()
        {
            var asGiven = new FrontierManager(Graph.Path(4));
            Assert.Equal(2, asGiven.MaxFrontierSize);

            // Same path, edges reordered so that edge (1,2) is decided last instead of in the middle.
            var reordered = new Graph(4, new[] { new Edge(0, 1), new Edge(2, 3), new Edge(1, 2) });
            var reorderedManager = new FrontierManager(reordered);

            Assert.Equal(3, reorderedManager.MaxFrontierSize);
            Assert.NotEqual(asGiven.MaxFrontierSize, reorderedManager.MaxFrontierSize);
        }

        [Fact]
        public void DisconnectedComponentsAreHandledIndependently()
        {
            // Two disjoint edges: (0,1) and (2,3).
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(2, 3) });
            var manager = new FrontierManager(graph);

            Assert.Equal(new[] { 0, 1 }, manager.IntroducedVertices(0));
            Assert.Equal(new[] { 0, 1 }, manager.ForgottenVertices(0));
            Assert.Equal(new[] { 2, 3 }, manager.IntroducedVertices(1));
            Assert.Equal(new[] { 2, 3 }, manager.ForgottenVertices(1));
            Assert.Equal(2, manager.MaxFrontierSize);
        }

        [Fact]
        public void IsolatedVerticesNeverAppearInTheFrontier()
        {
            // Vertex 2 has no incident edges.
            var graph = new Graph(3, new[] { new Edge(0, 1) });
            var manager = new FrontierManager(graph);

            Assert.DoesNotContain(2, manager.IntroducedVertices(0));
            Assert.DoesNotContain(2, manager.ForgottenVertices(0));
            Assert.Throws<ArgumentException>(() => manager.MateIndex(0, 2));
        }

        [Fact]
        public void GraphWithNoEdgesHasZeroFrontierEverywhere()
        {
            var graph = new Graph(3, Array.Empty<Edge>());
            var manager = new FrontierManager(graph);

            Assert.Equal(0, manager.MaxFrontierSize);
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.FrontierSize(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.IntroducedVertices(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.MateIndex(0, 0));
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new FrontierManager(null!));
        }

        [Fact]
        public void EdgeIndexOutOfRangeThrows()
        {
            var manager = new FrontierManager(Graph.Path(4));

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.FrontierSize(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.FrontierSize(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.IntroducedVertices(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.ForgottenVertices(3));
        }

        [Fact]
        public void VertexOutOfRangeThrows()
        {
            var manager = new FrontierManager(Graph.Path(4));

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.MateIndex(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.MateIndex(0, 4));
        }

        [Theory]
        [InlineData(2, 2)]
        [InlineData(3, 3)]
        [InlineData(4, 4)]
        [InlineData(50, 50)]
        public void ConstructionCompletesQuicklyForLargerGraphs(int rows, int cols)
        {
            var graph = Graph.Grid(rows, cols);

            var manager = new FrontierManager(graph);

            Assert.True(manager.MaxFrontierSize > 0);
            Assert.True(manager.MaxFrontierSize <= graph.VertexCount);
        }
    }
}

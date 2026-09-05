using System;
using System.Linq;
using Xunit;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M7-2 completion criteria for wiring <see cref="DirectedGraph.Optimize"/> /
    /// <see cref="DirectedGraph.EstimateMaxFrontierSize()"/> / <see cref="DirectedGraph.WithEdgeOrder"/> to
    /// the same <see cref="EdgeOrdering"/> logic <see cref="Graph"/> uses, now that both share
    /// <c>EdgeTopology</c>.
    /// </summary>
    public class DirectedGraphEdgeOrderTests
    {
        private static readonly EdgeOrderStrategy[] SupportedStrategies =
        {
            EdgeOrderStrategy.AsGiven,
            EdgeOrderStrategy.Bfs,
            EdgeOrderStrategy.Dfs,
            EdgeOrderStrategy.Grid,
            EdgeOrderStrategy.BeamSearchPathWidth,
        };

        public static TheoryData<EdgeOrderStrategy> Strategies()
        {
            var data = new TheoryData<EdgeOrderStrategy>();
            foreach (EdgeOrderStrategy strategy in SupportedStrategies)
            {
                data.Add(strategy);
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void OptimizeLeavesTheSourceGraphUntouchedAndReturnsAPermutation(EdgeOrderStrategy strategy)
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(4, 5));
            DirectedEdge[] before = graph.Edges.ToArray();

            DirectedGraph optimized = graph.Optimize(strategy);

            Assert.NotSame(graph, optimized);
            Assert.Null(graph.SourceOrder);
            Assert.Equal(before.Length, graph.EdgeCount);
            for (int i = 0; i < before.Length; i++)
            {
                Assert.Equal(before[i], graph.Edges[i]);
            }

            Assert.Equal(graph.VertexCount, optimized.VertexCount);
            AssertIsPermutationOfEdges(graph, optimized);
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void EdgeIndexMappingRoundTrips(EdgeOrderStrategy strategy)
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(4, 5));

            DirectedGraph optimized = graph.Optimize(strategy);
            DirectedEdgeOrderMapping mapping = Assert.IsType<DirectedEdgeOrderMapping>(optimized.SourceOrder);

            Assert.Same(graph, mapping.Source);
            Assert.Equal(graph.EdgeCount, mapping.Count);

            var sourceIndicesSeen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < optimized.EdgeCount; i++)
            {
                int sourceIndex = mapping.ToSourceEdgeIndex(i);
                Assert.True(sourceIndicesSeen.Add(sourceIndex));
                Assert.Equal(i, mapping.FromSourceEdgeIndex(sourceIndex));
                Assert.Equal(sourceIndex, mapping.ToSourceEdgeIndices[i]);
                Assert.Equal(graph.GetEdge(sourceIndex), optimized.GetEdge(i));
            }

            Assert.Equal(graph.EdgeCount, sourceIndicesSeen.Count);
        }

        [Theory]
        [InlineData(3, 3)]
        [InlineData(4, 5)]
        [InlineData(7, 7)]
        public void GridStrategyMatchesTheUndirectedGridsPeakFrontier(int rows, int cols)
        {
            // The frontier only ever holds vertices, and Bidirected doubles each edge into a consecutive
            // anti-parallel pair (see FrontierManagerTests.BidirectedGraphMaxFrontierSizeMatchesTheUndirectedGraph),
            // so the Grid strategy's peak frontier must be identical to the undirected grid's.
            Graph undirected = Graph.Grid(rows, cols);
            DirectedGraph directed = DirectedGraph.Grid(rows, cols);

            int undirectedWidth = undirected.EstimateMaxFrontierSize(EdgeOrderStrategy.Grid);
            int directedWidth = directed.EstimateMaxFrontierSize(EdgeOrderStrategy.Grid);

            Assert.Equal(undirectedWidth, directedWidth);
            Assert.True(directedWidth <= Math.Min(rows, cols) + 1);
        }

        [Fact]
        public void GridStrategyFallsBackToBfsOnAGraphThatIsNotAGrid()
        {
            DirectedGraph graph = DirectedGraph.Complete(6);

            DirectedGraph viaGrid = graph.Optimize(EdgeOrderStrategy.Grid);
            DirectedGraph viaBfs = graph.Optimize(EdgeOrderStrategy.Bfs);

            Assert.Equal(viaBfs.SourceOrder!.ToSourceEdgeIndices, viaGrid.SourceOrder!.ToSourceEdgeIndices);
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void EstimateMaxFrontierSizeAgreesWithFrontierManager(EdgeOrderStrategy strategy)
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(5, 7));

            DirectedGraph optimized = graph.Optimize(strategy);

            Assert.Equal(new FrontierManager(graph).MaxFrontierSize, graph.EstimateMaxFrontierSize());
            Assert.Equal(new FrontierManager(optimized).MaxFrontierSize, optimized.EstimateMaxFrontierSize());
            Assert.Equal(optimized.EstimateMaxFrontierSize(), graph.EstimateMaxFrontierSize(strategy));
        }

        [Theory]
        [MemberData(nameof(Strategies))]
        public void EveryStrategyHandlesDisconnectedGraphsAndIsolatedVertices(EdgeOrderStrategy strategy)
        {
            // Two components (a directed triangle and an anti-parallel pair) plus three vertices touching no arc.
            var graph = new DirectedGraph(9, new[]
            {
                new DirectedEdge(0, 1), new DirectedEdge(1, 2), new DirectedEdge(2, 0),
                new DirectedEdge(4, 5), new DirectedEdge(5, 4),
            });

            DirectedGraph optimized = graph.Optimize(strategy);

            AssertIsPermutationOfEdges(graph, optimized);
            Assert.Equal(graph.EstimateMaxFrontierSize(strategy), optimized.EstimateMaxFrontierSize());
        }

        [Fact]
        public void EveryStrategyHandlesAGraphWithNoEdges()
        {
            var graph = new DirectedGraph(4, Array.Empty<DirectedEdge>());

            foreach (EdgeOrderStrategy strategy in SupportedStrategies)
            {
                DirectedGraph optimized = graph.Optimize(strategy);

                Assert.Equal(0, optimized.EdgeCount);
                Assert.Equal(0, optimized.EstimateMaxFrontierSize());
            }
        }

        [Fact]
        public void WithEdgeOrderRejectsNull()
        {
            DirectedGraph graph = DirectedGraph.Path(4);

            Assert.Throws<ArgumentNullException>(() => graph.WithEdgeOrder(null!));
        }

        [Fact]
        public void WithEdgeOrderRejectsWrongLength()
        {
            DirectedGraph graph = DirectedGraph.Path(4);

            Assert.Throws<ArgumentException>(() => graph.WithEdgeOrder(new[] { 0, 1 }));
        }

        [Fact]
        public void WithEdgeOrderRejectsNonPermutation()
        {
            DirectedGraph graph = DirectedGraph.Path(4);

            Assert.Throws<ArgumentException>(() => graph.WithEdgeOrder(new[] { 0, 0, 2 }));
            Assert.Throws<ArgumentException>(() => graph.WithEdgeOrder(new[] { 0, 1, 3 }));
        }

        [Fact]
        public void DirectlyConstructedGraphHasNoSourceOrder()
        {
            Assert.Null(DirectedGraph.Path(4).SourceOrder);
        }

        private static void AssertIsPermutationOfEdges(DirectedGraph source, DirectedGraph reordered)
        {
            Assert.Equal(source.EdgeCount, reordered.EdgeCount);

            var seen = new bool[source.EdgeCount];
            DirectedEdgeOrderMapping mapping = reordered.SourceOrder!;
            for (int i = 0; i < reordered.EdgeCount; i++)
            {
                int sourceIndex = mapping.ToSourceEdgeIndex(i);
                Assert.False(seen[sourceIndex]);
                seen[sourceIndex] = true;
                Assert.Equal(source.GetEdge(sourceIndex), reordered.GetEdge(i));
            }

            Assert.DoesNotContain(false, seen);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M3-6 completion criteria for <see cref="VertexFrontierManager"/>: slot assignment and
    /// forget/earlier-neighbor bookkeeping against a hand-computed small graph, and — independently of the
    /// manager's own algorithm — an adjacency-only re-derivation of "which vertex retires which slot" that
    /// must agree with the manager on larger graphs (path, cycle, grid, disconnected components, isolated
    /// vertices).
    /// </summary>
    public class VertexFrontierManagerTests
    {
        // Path(4): edges (0,1) (1,2) (2,3). lastRelevant: v0->1, v1->2, v2->3, v3->3.
        [Fact]
        public void PathFourMatchesHandComputedFrontier()
        {
            var manager = new VertexFrontierManager(Graph.Path(4));

            Assert.Equal(0, manager.Slot(0));
            Assert.Equal(1, manager.Slot(1));
            Assert.Equal(0, manager.Slot(2)); // reuses vertex 0's freed slot
            Assert.Equal(1, manager.Slot(3)); // reuses vertex 1's freed slot

            Assert.Equal(Array.Empty<int>(), manager.EarlierNeighborSlots(0));
            Assert.Equal(new[] { 0 }, manager.EarlierNeighborSlots(1));
            Assert.Equal(new[] { 1 }, manager.EarlierNeighborSlots(2));
            Assert.Equal(new[] { 0 }, manager.EarlierNeighborSlots(3));

            Assert.Equal(Array.Empty<int>(), manager.ForgottenSlots(0));
            Assert.Equal(new[] { 0 }, manager.ForgottenSlots(1));
            Assert.Equal(new[] { 1 }, manager.ForgottenSlots(2));
            Assert.Equal(new[] { 0, 1 }, manager.ForgottenSlots(3));

            Assert.Equal(2, manager.MaxFrontierSize);
        }

        [Theory]
        [InlineData("path7")]
        [InlineData("cycle6")]
        [InlineData("grid3x3")]
        [InlineData("disconnected")]
        [InlineData("withIsolatedVertex")]
        public void SlotAssignmentMatchesAnIndependentAdjacencyReDerivation(string graphName)
        {
            Graph graph = NamedGraph(graphName);
            var manager = new VertexFrontierManager(graph);

            // Re-derive, from adjacency alone (never touching the manager's own bookkeeping), the last
            // vertex whose decision still needs each vertex's slot.
            var lastRelevant = new int[graph.VertexCount];
            for (int v = 0; v < graph.VertexCount; v++)
            {
                lastRelevant[v] = v;
                for (int i = 0; i < graph.IncidentEdges(v).Count; i++)
                {
                    int u = graph.GetEdge(graph.IncidentEdges(v)[i]).Other(v);
                    lastRelevant[v] = Math.Max(lastRelevant[v], u);
                }
            }

            // Simulate the frontier: replay vertices in ascending order, tracking which vertex currently
            // owns each slot, and check every manager answer against that simulation as we go.
            var ownerOfSlot = new Dictionary<int, int>();
            var slotOfLiveVertex = new Dictionary<int, int>();
            int maxLive = 0;

            for (int v = 0; v < graph.VertexCount; v++)
            {
                int slot = manager.Slot(v);
                Assert.False(ownerOfSlot.ContainsKey(slot), $"slot {slot} is already live when vertex {v} is introduced");
                ownerOfSlot[slot] = v;
                slotOfLiveVertex[v] = slot;
                maxLive = Math.Max(maxLive, slotOfLiveVertex.Count);

                // Earlier-neighbor slots must be exactly the live slots of v's lower-indexed neighbors.
                var expectedEarlier = new List<int>();
                for (int i = 0; i < graph.IncidentEdges(v).Count; i++)
                {
                    int u = graph.GetEdge(graph.IncidentEdges(v)[i]).Other(v);
                    if (u < v)
                    {
                        Assert.True(slotOfLiveVertex.ContainsKey(u), $"neighbor {u} of {v} should still be live");
                        expectedEarlier.Add(slotOfLiveVertex[u]);
                    }
                }

                Assert.Equal(expectedEarlier.OrderBy(x => x), manager.EarlierNeighborSlots(v).OrderBy(x => x));

                // Vertices forgotten here must be exactly those whose independently re-derived
                // lastRelevant equals v.
                var expectedForgottenVertices = Enumerable.Range(0, graph.VertexCount).Where(w => lastRelevant[w] == v).ToList();
                var expectedForgottenSlots = expectedForgottenVertices.Select(w => slotOfLiveVertex[w]).OrderBy(x => x).ToList();
                Assert.Equal(expectedForgottenSlots, manager.ForgottenSlots(v).OrderBy(x => x));

                foreach (int w in expectedForgottenVertices)
                {
                    ownerOfSlot.Remove(slotOfLiveVertex[w]);
                    slotOfLiveVertex.Remove(w);
                }
            }

            Assert.Empty(slotOfLiveVertex); // every vertex must eventually be forgotten
            Assert.Equal(maxLive, manager.MaxFrontierSize);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new VertexFrontierManager(null!));
        }

        [Fact]
        public void VertexOutOfRangeThrows()
        {
            var manager = new VertexFrontierManager(Graph.Path(4));

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Slot(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.Slot(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.EarlierNeighborSlots(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.ForgottenSlots(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.VertexToLevel(4));
        }

        [Fact]
        public void LevelOutOfRangeThrows()
        {
            var manager = new VertexFrontierManager(Graph.Path(4));

            Assert.Throws<ArgumentOutOfRangeException>(() => manager.LevelToVertex(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => manager.LevelToVertex(5));
        }

        [Fact]
        public void VertexToLevelAndLevelToVertexAreInverses()
        {
            var manager = new VertexFrontierManager(Graph.Path(4));

            for (int v = 0; v < 4; v++)
            {
                Assert.Equal(v, manager.LevelToVertex(manager.VertexToLevel(v)));
            }
        }

        private static Graph NamedGraph(string graphName) => graphName switch
        {
            "path7" => Graph.Path(7),
            "cycle6" => Graph.Cycle(6),
            "grid3x3" => Graph.Grid(3, 3),
            "disconnected" => new Graph(6, new[] { new Edge(0, 1), new Edge(1, 2), new Edge(3, 4), new Edge(4, 5) }),
            "withIsolatedVertex" => new Graph(4, new[] { new Edge(0, 1), new Edge(1, 3) }), // vertex 2 isolated
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };
    }
}

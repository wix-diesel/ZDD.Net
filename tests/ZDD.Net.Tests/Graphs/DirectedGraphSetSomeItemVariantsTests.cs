using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M8-4 coverage for the six one-item variants added to <see cref="DirectedGraphSet"/>.
    /// </summary>
    public class DirectedGraphSetSomeItemVariantsTests
    {
        private static readonly Edge[] UndirectedEdges =
        {
            new Edge(0, 1),
            new Edge(1, 2),
            new Edge(2, 3),
        };

        private static readonly DirectedEdge[] DirectedEdges =
        {
            new DirectedEdge(0, 1),
            new DirectedEdge(1, 2),
            new DirectedEdge(2, 3),
        };

        [Fact]
        public void EveryThreeArcFamilyMatchesGraphSetAndTheUnderlyingZdd()
        {
            var graph = new Graph(4, UndirectedEdges);
            var directedGraph = new DirectedGraph(4, DirectedEdges);

            for (int familyMask = 0; familyMask < 1 << (1 << 3); familyMask++)
            {
                IEnumerable<Edge[]> edgeSets = MemberMasks(familyMask).Select(ToEdges);
                IEnumerable<DirectedEdge[]> arcSets = MemberMasks(familyMask).Select(ToDirectedEdges);
                GraphSet undirected = GraphSet.FromSets(graph, edgeSets);
                DirectedGraphSet directed = DirectedGraphSet.FromSets(directedGraph, arcSets);

                AssertEquivalent(undirected.RemoveSomeItem(), directed.RemoveSomeItem());
                AssertEquivalent(undirected.AddSomeItem(), directed.AddSomeItem());
                AssertEquivalent(undirected.RemoveAddSomeItems(), directed.RemoveAddSomeItems());

                ReadOnlySpan<Edge> selectedEdges = new[] { UndirectedEdges[0], UndirectedEdges[2] };
                ReadOnlySpan<DirectedEdge> selectedArcs = new[] { DirectedEdges[0], DirectedEdges[2] };
                int[] selectedItems = { 0, 2 };

                DirectedGraphSet removed = directed.RemoveSomeItem(selectedArcs);
                DirectedGraphSet added = directed.AddSomeItem(selectedArcs);
                DirectedGraphSet swapped = directed.RemoveAddSomeItems(selectedArcs);

                AssertEquivalent(undirected.RemoveSomeItem(selectedEdges), removed);
                AssertEquivalent(undirected.AddSomeItem(selectedEdges), added);
                AssertEquivalent(undirected.RemoveAddSomeItems(selectedEdges), swapped);
                Assert.Equal(directed.Zdd.RemoveSomeItem(selectedItems), removed.Zdd);
                Assert.Equal(directed.Zdd.AddSomeItem(selectedItems), added.Zdd);
                Assert.Equal(directed.Zdd.RemoveAddSomeItems(selectedItems), swapped.Zdd);
            }
        }

        [Fact]
        public void RestrictedVariantsRejectAnArcOutsideTheGraph()
        {
            DirectedGraphSet family = DirectedGraphSet.PowerSet(new DirectedGraph(4, DirectedEdges));
            var foreign = new DirectedEdge(1, 0);

            Assert.Throws<ArgumentException>(() => family.RemoveSomeItem(foreign));
            Assert.Throws<ArgumentException>(() => family.AddSomeItem(foreign));
            Assert.Throws<ArgumentException>(() => family.RemoveAddSomeItems(foreign));
        }

        [Fact]
        public void ResultsRemainFilterableThroughThePrecomputedSpec()
        {
            DirectedGraph graph = new DirectedGraph(4, DirectedEdges);
            DirectedGraphSet family = DirectedGraphSet.FromSets(
                graph,
                new[]
                {
                    new[] { DirectedEdges[0] },
                    new[] { DirectedEdges[0], DirectedEdges[1] },
                });

            DirectedGraphSet transformed = family.AddSomeItem();

            Assert.All(transformed.Including(DirectedEdges[2]), set => Assert.Contains(DirectedEdges[2], set));
            Assert.All(transformed.Excluding(DirectedEdges[2]), set => Assert.DoesNotContain(DirectedEdges[2], set));
            Assert.Equal(
                transformed.Count,
                transformed.Including(DirectedEdges[2]).Count + transformed.Excluding(DirectedEdges[2]).Count);
        }

        private static IEnumerable<int> MemberMasks(int familyMask)
        {
            for (int setMask = 0; setMask < 1 << 3; setMask++)
            {
                if ((familyMask & (1 << setMask)) != 0)
                {
                    yield return setMask;
                }
            }
        }

        private static Edge[] ToEdges(int mask) =>
            UndirectedEdges.Where((_, index) => (mask & (1 << index)) != 0).ToArray();

        private static DirectedEdge[] ToDirectedEdges(int mask) =>
            DirectedEdges.Where((_, index) => (mask & (1 << index)) != 0).ToArray();

        private static void AssertEquivalent(GraphSet undirected, DirectedGraphSet directed)
        {
            int[] expected = undirected.Select(set => ToMask(set)).OrderBy(mask => mask).ToArray();
            int[] actual = directed.Select(set => ToMask(set)).OrderBy(mask => mask).ToArray();
            Assert.Equal(expected, actual);
        }

        private static int ToMask(IReadOnlySet<Edge> set)
        {
            int mask = 0;
            for (int i = 0; i < UndirectedEdges.Length; i++)
            {
                if (set.Contains(UndirectedEdges[i]))
                {
                    mask |= 1 << i;
                }
            }

            return mask;
        }

        private static int ToMask(IReadOnlySet<DirectedEdge> set)
        {
            int mask = 0;
            for (int i = 0; i < DirectedEdges.Length; i++)
            {
                if (set.Contains(DirectedEdges[i]))
                {
                    mask |= 1 << i;
                }
            }

            return mask;
        }
    }
}

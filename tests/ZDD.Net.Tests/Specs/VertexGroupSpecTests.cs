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
    /// M6-14 completion criteria for <see cref="VertexGroupSpec"/>: matches brute-force enumeration on
    /// small graphs across several group patterns (including zero groups, an empty group, and singleton
    /// groups), every enumerated set actually keeps each group in its own component and different groups
    /// apart, a single group matches <see cref="ConnectedSubgraphSpec"/> exactly, a free (ungrouped) vertex
    /// may join at most one group but never bridges two, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class VertexGroupSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationForVariousGroupPatterns(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);

            foreach (IReadOnlyList<IReadOnlyList<int>> groups in GroupPatterns(graph.VertexCount))
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<VertexGroupSpec>(manager, new VertexGroupSpec(graph, groups));

                BruteForceFamily expected = BruteForceVertexGroups(graph, groups);

                FamilyAssert.AssertSameFamily($"{graphName} groups={Describe(groups)}", built, expected);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MatchesBruteForceEnumerationOnRandomGraphs(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 6, extraEdgeProbability: 0.3, seed);

            foreach (IReadOnlyList<IReadOnlyList<int>> groups in GroupPatterns(graph.VertexCount))
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<VertexGroupSpec>(manager, new VertexGroupSpec(graph, groups));

                BruteForceFamily expected = BruteForceVertexGroups(graph, groups);

                FamilyAssert.AssertSameFamily($"seed={seed} groups={Describe(groups)}", built, expected);
            }
        }

        [Fact]
        public void EveryEnumeratedSetSatisfiesGroupConstraints()
        {
            Graph graph = Graph.Grid(3, 3);
            IReadOnlyList<IReadOnlyList<int>> groups = new IReadOnlyList<int>[]
            {
                new[] { 0, 4 },
                new[] { 2, 6 },
            };

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<VertexGroupSpec>(manager, new VertexGroupSpec(graph, groups));

            Assert.True(built.Count > BigInteger.Zero);
            foreach (int[] edgeSet in built.Sets())
            {
                Assert.True(SatisfiesVertexGroups(graph, edgeSet, groups));
            }
        }

        [Fact]
        public void SingleGroupMatchesConnectedSubgraphSpec()
        {
            Graph graph = Graph.Grid(3, 3);
            int[] terminals = { 0, 4, 8 };
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd vertexGroups = FrontierBuilder.Build<VertexGroupSpec>(
                manager, new VertexGroupSpec(graph, new IReadOnlyList<int>[] { terminals }));
            Zdd connected = FrontierBuilder.Build<ConnectedSubgraphSpec>(
                manager, new ConnectedSubgraphSpec(graph, terminals));

            Assert.Equal(connected, vertexGroups);
        }

        [Fact]
        public void ZeroGroupsAcceptsEveryEdgeSubset()
        {
            Graph graph = Graph.Path(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<VertexGroupSpec>(
                manager, new VertexGroupSpec(graph, Array.Empty<IReadOnlyList<int>>()));

            Assert.Equal(BigInteger.Pow(2, graph.EdgeCount), built.Count);
        }

        [Fact]
        public void EmptyGroupIsVacuous()
        {
            Graph graph = Graph.Grid(2, 3);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd withEmptyGroup = FrontierBuilder.Build<VertexGroupSpec>(
                manager, new VertexGroupSpec(graph, new IReadOnlyList<int>[] { Array.Empty<int>(), new[] { 0, 5 } }));
            Zdd withoutEmptyGroup = FrontierBuilder.Build<VertexGroupSpec>(
                manager, new VertexGroupSpec(graph, new IReadOnlyList<int>[] { new[] { 0, 5 } }));

            Assert.Equal(withoutEmptyGroup, withEmptyGroup);
        }

        [Fact]
        public void SingletonGroupsForceMutualDisconnection()
        {
            // A triangle 0-1-2 with two singleton groups {0} and {1}: 0 and 1 must never end up in the same
            // component, even though each group is individually trivial (one vertex is always "connected").
            // By hand: of the 8 edge subsets, only {}, {(1,2)}, {(2,0)} keep 0 and 1 apart; every subset that
            // includes edge (0,1) directly, or connects both to 2, merges them.
            var graph = new Graph(3, new[] { new Edge(0, 1), new Edge(1, 2), new Edge(2, 0) });
            IReadOnlyList<IReadOnlyList<int>> groups = new IReadOnlyList<int>[] { new[] { 0 }, new[] { 1 } };

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<VertexGroupSpec>(manager, new VertexGroupSpec(graph, groups));

            BruteForceFamily expected = BruteForceFamily.FromMasks(3, new[]
            {
                BruteForceFamily.MaskOf(3), // {}
                BruteForceFamily.MaskOf(3, 1), // {(1,2)}
                BruteForceFamily.MaskOf(3, 2), // {(2,0)}
            });

            FamilyAssert.AssertSameFamily(built, expected);
        }

        [Fact]
        public void FreeVertexNeverBridgesTwoGroups()
        {
            // A star: c is free, a1/a2 form group A, b1/b2 form group B. Edges c-a1, c-a2, c-b1, c-b2. Any
            // accepted set that connects both of A through c and both of B through c would put a1 and b1 in
            // the same component, which is forbidden — c may serve at most one group.
            var graph = new Graph(5, new[]
            {
                new Edge(0, 1), // c-a1
                new Edge(0, 2), // c-a2
                new Edge(0, 3), // c-b1
                new Edge(0, 4), // c-b2
            });
            IReadOnlyList<IReadOnlyList<int>> groups = new IReadOnlyList<int>[] { new[] { 1, 2 }, new[] { 3, 4 } };

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<VertexGroupSpec>(manager, new VertexGroupSpec(graph, groups));

            BruteForceFamily expected = BruteForceVertexGroups(graph, groups);
            FamilyAssert.AssertSameFamily(built, expected);

            foreach (int[] edgeSet in built.Sets())
            {
                bool hasA1 = edgeSet.Contains(0);
                bool hasA2 = edgeSet.Contains(1);
                bool hasB1 = edgeSet.Contains(2);
                bool hasB2 = edgeSet.Contains(3);
                Assert.False(hasA1 && hasA2 && (hasB1 || hasB2), "c may not bridge group A's members to group B's");
                Assert.False(hasB1 && hasB2 && (hasA1 || hasA2), "c may not bridge group B's members to group A's");
            }
        }

        [Fact]
        public void GroupSpanningDisconnectedHalvesIsEmpty()
        {
            // Two disjoint triangles: vertices 0-1-2 and 3-4-5, no edge between the halves.
            var graph = new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<VertexGroupSpec>(
                manager, new VertexGroupSpec(graph, new IReadOnlyList<int>[] { new[] { 0, 3 } }));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedVertexInNontrivialGroupIsEmpty()
        {
            // Vertex 3 has no incident edges at all.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<VertexGroupSpec>(
                manager, new VertexGroupSpec(graph, new IReadOnlyList<int>[] { new[] { 0, 3 } }));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedVertexInSingletonGroupIsFine()
        {
            // Vertex 3 has no incident edges, but its group has only itself in it.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<VertexGroupSpec>(
                manager, new VertexGroupSpec(graph, new IReadOnlyList<int>[] { new[] { 3 } }));

            Assert.Equal(BigInteger.Pow(2, graph.EdgeCount), built.Count);
        }

        [Fact]
        public void GroupsPropertyReturnsNormalizedGroups()
        {
            Graph graph = Graph.Path(6);
            var spec = new VertexGroupSpec(graph, new IReadOnlyList<int>[] { new[] { 4, 0 }, Array.Empty<int>(), new[] { 2 } });

            Assert.Equal(3, spec.GroupCount);
            Assert.Equal(new[] { 0, 4 }, spec.Groups[0]);
            Assert.Empty(spec.Groups[1]);
            Assert.Equal(new[] { 2 }, spec.Groups[2]);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new VertexGroupSpec(null!, Array.Empty<IReadOnlyList<int>>()));
        }

        [Fact]
        public void ConstructorRejectsNullGroups()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentNullException>(() => new VertexGroupSpec(graph, null!));
        }

        [Fact]
        public void ConstructorRejectsNullGroupEntry()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentNullException>(() => new VertexGroupSpec(graph, new IReadOnlyList<int>[] { null! }));
        }

        [Fact]
        public void ConstructorRejectsOutOfRangeVertex()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new VertexGroupSpec(graph, new IReadOnlyList<int>[] { new[] { 0, 4 } }));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new VertexGroupSpec(graph, new IReadOnlyList<int>[] { new[] { -1 } }));
        }

        [Fact]
        public void ConstructorRejectsVertexRepeatedWithinGroup()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentException>(
                () => new VertexGroupSpec(graph, new IReadOnlyList<int>[] { new[] { 0, 1, 0 } }));
        }

        [Fact]
        public void ConstructorRejectsVertexRepeatedAcrossGroups()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentException>(
                () => new VertexGroupSpec(graph, new IReadOnlyList<int>[] { new[] { 0, 1 }, new[] { 1, 2 } }));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new VertexGroupSpec(grid, new IReadOnlyList<int>[] { new[] { 0, 5 }, new[] { 10, 15 } });
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(VertexGroupSpec spec, Span<int> state, int level)
            {
                while (level > 0)
                {
                    level = spec.GetChild(state, level, 1);
                    if (DdResult.IsTerminal(level))
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// A handful of group-set shapes: none, one trivial singleton, one small group mixed with an empty
        /// group, "every vertex in one group", several small groups (when the graph is big enough), and a
        /// pattern that leaves some vertices free (when the graph is big enough).
        /// </summary>
        private static IEnumerable<IReadOnlyList<IReadOnlyList<int>>> GroupPatterns(int vertexCount)
        {
            yield return Array.Empty<IReadOnlyList<int>>();
            yield return new IReadOnlyList<int>[] { new[] { 0 } };
            yield return new IReadOnlyList<int>[] { new[] { 0 }, new[] { vertexCount - 1 } };
            yield return new IReadOnlyList<int>[] { Array.Empty<int>(), new[] { 0, vertexCount - 1 } };
            yield return new IReadOnlyList<int>[] { Enumerable.Range(0, vertexCount).ToArray() };

            if (vertexCount >= 4)
            {
                yield return new IReadOnlyList<int>[] { new[] { 0, 1 }, new[] { 2, 3 } };
            }

            if (vertexCount >= 5)
            {
                yield return new IReadOnlyList<int>[] { new[] { 0 }, new[] { 1, 2 }, new[] { 3, 4 } };
            }

            if (vertexCount >= 6)
            {
                yield return new IReadOnlyList<int>[] { new[] { 0, 2 }, new[] { 3, 5 } }; // 1, 4 left free
            }
        }

        private static string Describe(IReadOnlyList<IReadOnlyList<int>> groups) =>
            string.Join(";", groups.Select(g => string.Join(",", g)));

        /// <summary>Whether every group in <paramref name="groups"/> ends up as its own component under <paramref name="edgeSet"/>.</summary>
        private static bool SatisfiesVertexGroups(Graph graph, IReadOnlyList<int> edgeSet, IReadOnlyList<IReadOnlyList<int>> groups)
        {
            var parent = new int[graph.VertexCount];
            for (int v = 0; v < graph.VertexCount; v++)
            {
                parent[v] = v;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                parent[Find(edge.U)] = Find(edge.V);
            }

            var rootGroup = new Dictionary<int, int>();
            for (int g = 0; g < groups.Count; g++)
            {
                IReadOnlyList<int> group = groups[g];
                if (group.Count == 0)
                {
                    continue;
                }

                int root = Find(group[0]);
                for (int i = 1; i < group.Count; i++)
                {
                    if (Find(group[i]) != root)
                    {
                        return false; // this group's members are split across two components
                    }
                }

                if (rootGroup.TryGetValue(root, out int owner) && owner != g)
                {
                    return false; // this component already belongs to a different group
                }

                rootGroup[root] = g;
            }

            return true;
        }

        private static BruteForceFamily BruteForceVertexGroups(Graph graph, IReadOnlyList<IReadOnlyList<int>> groups)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceVertexGroups enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
                    nameof(graph));
            }

            int bound = 1 << edgeCount;

            for (int mask = 0; mask < bound; mask++)
            {
                var edgeSet = new List<int>();
                for (int i = 0; i < edgeCount; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        edgeSet.Add(i);
                    }
                }

                if (SatisfiesVertexGroups(graph, edgeSet, groups))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }
    }
}

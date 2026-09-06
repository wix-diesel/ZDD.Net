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
    /// M7-5 completion criteria for <see cref="ArborescenceSpec"/>: the count matches the directed matrix-tree
    /// theorem (<see cref="Kirchhoff.CountArborescences"/>) computed independently of the ZDD, on grid and
    /// random graphs — the directed analogue of how <see cref="SpanningTreeSpec"/> is checked against
    /// Kirchhoff's own theorem; <see cref="DirectedGraph.Bidirected"/>'s arborescence count at any fixed root
    /// equals the undirected graph's own spanning tree count (fixing a root orients each spanning tree
    /// exactly one way); an arc into <see cref="ArborescenceSpec.Root"/> is never selected; a graph with a
    /// vertex unreachable from the root builds to <c>Empty</c> when <see cref="ArborescenceSpec.Spanning"/>;
    /// matches brute-force enumeration on small directed graphs with every enumerated set verified as an
    /// actual arborescence; and <c>GetChild</c> does not allocate.
    /// </summary>
    public class ArborescenceSpecTests
    {
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void CountMatchesDirectedMatrixTreeTheoremForCompleteGraphs(int n)
        {
            DirectedGraph graph = DirectedGraph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            for (int root = 0; root < n; root++)
            {
                Zdd built = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root));
                Assert.Equal(Kirchhoff.CountArborescences(graph, root), built.Count);
            }
        }

        [Theory]
        [InlineData(2, 2)]
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        public void CountMatchesDirectedMatrixTreeTheoremForBidirectedGridGraphs(int rows, int cols)
        {
            DirectedGraph graph = DirectedGraph.Grid(rows, cols);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0));
            Assert.Equal(Kirchhoff.CountArborescences(graph, 0), built.Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void CountMatchesDirectedMatrixTreeTheoremForRandomReachableGraphs(int seed)
        {
            DirectedGraph graph = RandomReachableDirectedGraph(vertexCount: 7, extraArcProbability: 0.2, seed);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0));
            Assert.Equal(Kirchhoff.CountArborescences(graph, 0), built.Count);
        }

        [Theory]
        [InlineData("complete4")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        [InlineData("cycle5")]
        public void BidirectedArborescenceCountEqualsUndirectedSpanningTreeCountAtAnyRoot(string graphName)
        {
            Graph undirected = NamedUndirectedGraph(graphName);
            DirectedGraph directed = DirectedGraph.Bidirected(undirected);
            BigInteger expected = Kirchhoff.CountSpanningTrees(undirected);

            using ZddManager manager = new ZddManager(directed.EdgeCount);

            for (int root = 0; root < directed.VertexCount; root++)
            {
                Zdd built = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(directed, root));
                Assert.Equal(expected, built.Count);
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        [InlineData("oneWayTree")]
        public void MatchesBruteForceEnumerationOnSmallGraphsForBothModes(string graphName)
        {
            (DirectedGraph graph, int root) = DirectedGraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd spanning = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root, spanning: true));
            FamilyAssert.AssertSameFamily($"{graphName} spanning", spanning, BruteForceArborescences(graph, root, spanning: true));

            Zdd nonSpanning = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root, spanning: false));
            FamilyAssert.AssertSameFamily($"{graphName} non-spanning", nonSpanning, BruteForceArborescences(graph, root, spanning: false));

            // Every full-spanning arborescence is trivially a valid non-spanning one too (it just happens to
            // touch every vertex), so the spanning family is always contained in the non-spanning one.
            Assert.Equal(manager.Empty, spanning.Difference(nonSpanning));
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("complete5")]
        public void EveryEnumeratedSpanningSetIsAValidArborescence(string graphName)
        {
            (DirectedGraph graph, int root) = DirectedGraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root));

            foreach (int[] arcSet in built.Sets())
            {
                Assert.True(
                    IsArborescenceMember(graph, arcSet, root, spanning: true),
                    "every enumerated arc set must be a valid spanning out-arborescence");
            }
        }

        [Fact]
        public void ArcIntoRootIsNeverSelected()
        {
            // Vertex 0 is root; arc 2 -> 0 exists in the graph but must never appear in any enumerated set.
            var graph = new DirectedGraph(3, new[]
            {
                new DirectedEdge(0, 1), new DirectedEdge(1, 2), new DirectedEdge(2, 0), new DirectedEdge(0, 2),
            });
            int intoRootArcIndex = Array.IndexOf(graph.Edges.ToArray(), new DirectedEdge(2, 0));

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd spanning = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0, spanning: true));
            Zdd nonSpanning = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0, spanning: false));

            foreach (Zdd built in new[] { spanning, nonSpanning })
            {
                foreach (int[] arcSet in built.Sets())
                {
                    Assert.DoesNotContain(intoRootArcIndex, arcSet);
                }
            }
        }

        [Fact]
        public void UnreachableVertexIsEmptyWhenSpanningButNotOtherwise()
        {
            // Root 0 reaches vertex 1 (0 -> 1), but vertex 2's only arc is 2 -> 1: nothing ever reaches 2.
            var graph = new DirectedGraph(3, new[] { new DirectedEdge(0, 1), new DirectedEdge(2, 1) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd spanning = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0, spanning: true));
            Zdd nonSpanning = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0, spanning: false));

            Assert.Equal(manager.Empty, spanning);
            Assert.NotEqual(manager.Empty, nonSpanning); // e.g. just {0 -> 1}, or the empty arc set

            foreach (int[] arcSet in nonSpanning.Sets())
            {
                Assert.True(IsArborescenceMember(graph, arcSet, root: 0, spanning: false));
            }
        }

        [Fact]
        public void SingleVertexIsTheBaseFamilyInEitherMode()
        {
            var graph = new DirectedGraph(1, Array.Empty<DirectedEdge>());
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd spanning = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0, spanning: true));
            Zdd nonSpanning = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0, spanning: false));

            Assert.Equal(BigInteger.One, spanning.Count);
            Assert.Equal(BigInteger.One, nonSpanning.Count);
        }

        [Fact]
        public void DisconnectedGraphIsEmptyWhenSpanning()
        {
            // Two disjoint triangles: 0-1-2 and 3-4-5, no arc between the halves.
            DirectedGraph graph = DirectedGraph.Bidirected(new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            }));

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<ArborescenceSpec>(manager, new ArborescenceSpec(graph, root: 0, spanning: true));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new ArborescenceSpec(null!, root: 0));
        }

        [Fact]
        public void ConstructorRejectsRootOutOfRange()
        {
            DirectedGraph graph = DirectedGraph.Path(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArborescenceSpec(graph, root: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArborescenceSpec(graph, root: 3));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GetChildDoesNotAllocate(bool spanning)
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(4, 4));
            var spec = new ArborescenceSpec(graph, root: 0, spanning);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(ArborescenceSpec spec, Span<int> state, int level)
            {
                while (level > 0)
                {
                    level = spec.GetChild(state, level, 0);
                    if (DdResult.IsTerminal(level))
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Checks that <paramref name="arcSet"/> forms a valid out-arborescence rooted at <paramref name="root"/>:
        /// acyclic, <paramref name="root"/> has in-degree 0, every other vertex has in-degree at most 1, at
        /// most one connected component ever gains an arc and it is the one containing <paramref name="root"/>
        /// (so no second, disconnected tree), and — when <paramref name="spanning"/> — that component covers
        /// every vertex. The tree property (edges = size - 1 per component) falls out of the cycle check for
        /// free, since a union-find that rejects same-component arcs can never let a component's edge count
        /// exceed its size minus one.
        /// </summary>
        internal static bool IsArborescenceMember(DirectedGraph graph, IReadOnlyList<int> arcSet, int root, bool spanning)
        {
            int n = graph.VertexCount;
            var parent = Enumerable.Range(0, n).ToArray();
            var size = Enumerable.Repeat(1, n).ToArray();

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra != rb)
                {
                    parent[ra] = rb;
                    size[rb] += size[ra];
                }
            }

            var inDegree = new int[n];
            foreach (int edgeIndex in arcSet)
            {
                DirectedEdge arc = graph.GetEdge(edgeIndex);
                inDegree[arc.To]++;
                int ru = Find(arc.From);
                int rv = Find(arc.To);
                if (ru == rv)
                {
                    return false; // cycle
                }

                Union(ru, rv);
            }

            if (inDegree[root] != 0)
            {
                return false;
            }

            for (int v = 0; v < n; v++)
            {
                if (v != root && inDegree[v] > 1)
                {
                    return false;
                }
            }

            int rootGroup = Find(root);
            for (int v = 0; v < n; v++)
            {
                int group = Find(v);
                if (size[group] > 1 && group != rootGroup)
                {
                    return false; // a second, disconnected real tree
                }
            }

            return !spanning || size[rootGroup] == n;
        }

        private static BruteForceFamily BruteForceArborescences(DirectedGraph graph, int root, bool spanning)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceArborescences enumerates all 2^edgeCount subsets and cannot handle {edgeCount} arcs.",
                    nameof(graph));
            }

            int bound = 1 << edgeCount;

            for (int mask = 0; mask < bound; mask++)
            {
                var arcSet = new List<int>();
                for (int i = 0; i < edgeCount; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        arcSet.Add(i);
                    }
                }

                if (IsArborescenceMember(graph, arcSet, root, spanning))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static Graph NamedUndirectedGraph(string graphName) => graphName switch
        {
            "complete4" => Graph.Complete(4),
            "complete5" => Graph.Complete(5),
            "grid2x3" => Graph.Grid(2, 3),
            "cycle5" => Graph.Cycle(5),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static (DirectedGraph Graph, int Root) DirectedGraphFor(string graphName) => graphName switch
        {
            "path4" => (DirectedGraph.Bidirected(Graph.Path(4)), 0),
            "cycle5" => (DirectedGraph.Bidirected(Graph.Cycle(5)), 0),
            "complete5" => (DirectedGraph.Bidirected(Graph.Complete(5)), 2),
            "grid2x3" => (DirectedGraph.Bidirected(Graph.Grid(2, 3)), 1),
            // A one-way tree plus a stray reversed arc: root 0 can reach 1 and 2, but 3's only arc points
            // away from it toward 1, so 3 is unreachable — non-spanning mode must still find members.
            "oneWayTree" => (new DirectedGraph(4, new[]
            {
                new DirectedEdge(0, 1), new DirectedEdge(0, 2), new DirectedEdge(3, 1),
            }), 0),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        /// <summary>
        /// Builds a directed graph on <paramref name="vertexCount"/> vertices guaranteed reachable from
        /// vertex 0: a random backbone arc into each vertex from some earlier one (in a random order), plus
        /// extra random arcs in either direction for density.
        /// </summary>
        private static DirectedGraph RandomReachableDirectedGraph(int vertexCount, double extraArcProbability, int seed)
        {
            var random = new Random(seed);
            var edges = new List<DirectedEdge>();
            var seen = new HashSet<(int, int)>();

            void AddArc(int u, int v)
            {
                if (seen.Add((u, v)))
                {
                    edges.Add(new DirectedEdge(u, v));
                }
            }

            var rest = Enumerable.Range(1, vertexCount - 1).OrderBy(_ => random.Next()).ToArray();
            var order = new int[vertexCount];
            order[0] = 0;
            Array.Copy(rest, 0, order, 1, rest.Length);

            for (int i = 1; i < vertexCount; i++)
            {
                int j = random.Next(i);
                AddArc(order[j], order[i]);
            }

            for (int u = 0; u < vertexCount; u++)
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    if (u != v && random.NextDouble() < extraArcProbability)
                    {
                        AddArc(u, v);
                    }
                }
            }

            return new DirectedGraph(vertexCount, edges);
        }
    }
}

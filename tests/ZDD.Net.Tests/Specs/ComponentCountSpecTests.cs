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
    /// M6-15 completion criteria for <see cref="ComponentCountSpec"/>: matches brute-force enumeration on
    /// small graphs across every reachable target, every enumerated set really has that many non-trivial
    /// (edge-bearing) connected components, an isolated vertex &#8212; whether the graph gives it no edge at
    /// all, or it simply ends up with none selected &#8212; never counts as a component of its own, a target
    /// of 0 matches exactly the empty edge set, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class ComponentCountSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationForEveryTarget(string graphName)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);

            for (int target = 0; target <= graph.EdgeCount; target++)
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<ComponentCountSpec>(manager, new ComponentCountSpec(graph, target));

                BruteForceFamily expected = BruteForceComponentCount(graph, target);

                FamilyAssert.AssertSameFamily($"{graphName} target={target}", built, expected);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void MatchesBruteForceEnumerationOnRandomGraphs(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 6, extraEdgeProbability: 0.3, seed);

            for (int target = 0; target <= graph.EdgeCount; target++)
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<ComponentCountSpec>(manager, new ComponentCountSpec(graph, target));

                BruteForceFamily expected = BruteForceComponentCount(graph, target);

                FamilyAssert.AssertSameFamily($"seed={seed} target={target}", built, expected);
            }
        }

        [Theory]
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        public void EveryEnumeratedGridSetHasExactlyThatManyNonTrivialComponents(int rows, int cols)
        {
            Graph graph = Graph.Grid(rows, cols);

            for (int target = 0; target <= graph.EdgeCount; target++)
            {
                using ZddManager manager = new ZddManager(graph.EdgeCount);
                Zdd built = FrontierBuilder.Build<ComponentCountSpec>(manager, new ComponentCountSpec(graph, target));

                foreach (int[] edgeSet in built.Sets())
                {
                    Assert.Equal(target, CountNonTrivialComponents(graph, edgeSet));
                }
            }
        }

        [Fact]
        public void IsolatedGraphVertexIsNeverCounted()
        {
            // Vertex 3 has no incident edges at all: a single edge among 0-1-2 already makes one
            // non-trivial component, and vertex 3 never adds a second, no matter what.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd oneComponent = FrontierBuilder.Build<ComponentCountSpec>(manager, new ComponentCountSpec(graph, target: 1));

            // Both edges taken (one path 0-1-2, vertex 3 isolated) and just one edge taken (the other
            // vertex among {0,1,2} left isolated, plus vertex 3) both count as exactly one component.
            Assert.True(oneComponent.Contains(new[] { 0, 1 }));
            Assert.True(oneComponent.Contains(new[] { 0 }));
            Assert.True(oneComponent.Contains(new[] { 1 }));

            // No edges at all: nothing is non-trivial, so this belongs to target 0, not target 1.
            Assert.False(oneComponent.Contains(Array.Empty<int>()));

            using ZddManager manager0 = new ZddManager(graph.EdgeCount);
            Zdd zeroComponents = FrontierBuilder.Build<ComponentCountSpec>(manager0, new ComponentCountSpec(graph, target: 0));
            Assert.Equal(BigInteger.One, zeroComponents.Count);
            Assert.Equal(Array.Empty<int>(), Assert.Single(zeroComponents.Sets()));
        }

        [Fact]
        public void ChoiceIsolatedVertexIsNeverCounted()
        {
            // A star: vertex 0 has edges to 1, 2 and 3, all of which have edges available in the
            // graph. Choosing only edge (0,1) leaves 2 and 3 both edge-less in this particular member,
            // even though the graph itself gives them edges to choose from — they still don't count.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(0, 2), new Edge(0, 3) });

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd oneComponent = FrontierBuilder.Build<ComponentCountSpec>(manager, new ComponentCountSpec(graph, target: 1));

            Assert.True(oneComponent.Contains(new[] { 0 })); // only (0,1) taken: one non-trivial component, 2 and 3 ignored
            Assert.True(oneComponent.Contains(new[] { 0, 1 })); // (0,1) and (0,2): still just one component (star), 3 ignored
            Assert.True(oneComponent.Contains(new[] { 0, 1, 2 })); // every edge: still one star-shaped component
        }

        [Fact]
        public void TargetMustBeNonNegative()
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ComponentCountSpec(graph, -1));
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new ComponentCountSpec(null!, 0));
        }

        [Fact]
        public void EdgelessGraphOnlyAcceptsTargetZero()
        {
            Graph graph = new Graph(3, Array.Empty<Edge>());

            using ZddManager zero = new ZddManager(graph.EdgeCount);
            Zdd built0 = FrontierBuilder.Build<ComponentCountSpec>(zero, new ComponentCountSpec(graph, 0));
            Assert.Equal(BigInteger.One, built0.Count);

            using ZddManager one = new ZddManager(graph.EdgeCount);
            Zdd built1 = FrontierBuilder.Build<ComponentCountSpec>(one, new ComponentCountSpec(graph, 1));
            Assert.Equal(one.Empty, built1);
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new ComponentCountSpec(grid, target: 1);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(ComponentCountSpec spec, Span<int> state, int level)
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

        /// <summary>The number of connected components with at least one edge (size &#8805; 2), ignoring isolated vertices.</summary>
        private static int CountNonTrivialComponents(Graph graph, IReadOnlyCollection<int> edgeSet)
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

            var sizeByRoot = new Dictionary<int, int>();
            for (int v = 0; v < graph.VertexCount; v++)
            {
                int root = Find(v);
                sizeByRoot[root] = sizeByRoot.GetValueOrDefault(root) + 1;
            }

            return sizeByRoot.Values.Count(size => size >= 2);
        }

        private static BruteForceFamily BruteForceComponentCount(Graph graph, int target)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceComponentCount enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                if (CountNonTrivialComponents(graph, edgeSet) == target)
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }
    }
}

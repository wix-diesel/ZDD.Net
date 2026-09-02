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
    /// M3-4 completion criteria for <see cref="CycleSpec"/>: <see cref="Graph.Cycle"/> has exactly one
    /// cycle, <see cref="Graph.Complete"/>'s total simple-cycle count matches the known formula, matches
    /// brute-force enumeration on small graphs, every enumerated set really is a valid cycle family member,
    /// <see cref="CycleSpec.Single"/> is a subset of the non-<see cref="CycleSpec.Single"/> family, and
    /// <c>GetChild</c> does not allocate.
    /// </summary>
    public class CycleSpecTests
    {
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(9)]
        public void CycleGraphHasExactlyOneCycleInEitherMode(int n)
        {
            Graph graph = Graph.Cycle(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd single = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: true));
            Zdd multi = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: false));

            Assert.Equal(BigInteger.One, single.Count);
            Assert.Equal(BigInteger.One, multi.Count);
            Assert.Equal(single, multi); // the whole graph's own cycle is the only member of either family
        }

        // Number of simple cycles in K_n: sum over cycle length k = 3 .. n of C(n, k) * (k-1)! / 2
        // (choose the k vertices, then count distinct cyclic arrangements of them).
        [Theory]
        [InlineData(3, "1")]
        [InlineData(4, "7")]
        [InlineData(5, "37")]
        [InlineData(6, "197")]
        [InlineData(7, "1172")]
        [InlineData(8, "8018")]
        public void SingleCycleCountOnCompleteGraphMatchesKnownFormula(int n, string expected)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: true));

            Assert.Equal(BigInteger.Parse(expected), built.Count);
            Assert.Equal(BigInteger.Parse(expected), CountSimpleCyclesInCompleteGraphFormula(n));
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("twoTriangles")]
        public void MatchesBruteForceEnumerationOnSmallGraphsForBothModes(string graphName)
        {
            Graph graph = GraphFor(graphName);

            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd single = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: true));
            BruteForceFamily expectedSingle = BruteForceCycles(graph, single: true);
            FamilyAssert.AssertSameFamily($"{graphName} single", single, expectedSingle);

            Zdd multi = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: false));
            BruteForceFamily expectedMulti = BruteForceCycles(graph, single: false);
            FamilyAssert.AssertSameFamily($"{graphName} multi", multi, expectedMulti);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("twoTriangles")]
        public void EverySingleCycleIsOneSimpleCycle(string graphName)
        {
            Graph graph = GraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: true));

            foreach (int[] edgeSet in built.Sets())
            {
                AssertIsValidCycleFamilyMember(graph, edgeSet, requireSingleComponent: true);
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("twoTriangles")]
        public void EveryMultiCycleIsADisjointUnionOfSimpleCycles(string graphName)
        {
            Graph graph = GraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: false));

            foreach (int[] edgeSet in built.Sets())
            {
                AssertIsValidCycleFamilyMember(graph, edgeSet, requireSingleComponent: false);
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("twoTriangles")]
        public void SingleIsASubsetOfMulti(string graphName)
        {
            Graph graph = GraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd single = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: true));
            Zdd multi = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: false));

            Assert.Equal(manager.Empty, single.Difference(multi));
        }

        [Fact]
        public void GraphWithNoEdgesIsEmptyInEitherMode()
        {
            var graph = new Graph(3, Array.Empty<Edge>());
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Assert.Equal(manager.Empty, FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: true)));
            Assert.Equal(manager.Empty, FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: false)));
        }

        [Fact]
        public void TreeGraphHasNoCyclesInEitherMode()
        {
            Graph graph = Graph.Path(5);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Assert.Equal(manager.Empty, FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: true)));
            Assert.Equal(manager.Empty, FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: false)));
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new CycleSpec(null!));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph graph = Graph.Grid(4, 4);
            var spec = new CycleSpec(graph, single: false);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(CycleSpec spec, Span<int> state, int level)
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

        private static Graph GraphFor(string graphName) => graphName switch
        {
            "path4" => Graph.Path(4),
            "cycle5" => Graph.Cycle(5),
            "complete5" => Graph.Complete(5),
            // Two disjoint triangles: the only way "multi" and "single" families can actually differ,
            // since a single triangle graph has nothing left over to form a second cycle.
            "twoTriangles" => new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static void AssertIsValidCycleFamilyMember(Graph graph, int[] edgeSet, bool requireSingleComponent)
        {
            Assert.True(edgeSet.Length >= 1, "a cycle family member must use at least one edge");

            var degree = new int[graph.VertexCount];
            var parent = Enumerable.Range(0, graph.VertexCount).ToArray();

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
                degree[edge.U]++;
                degree[edge.V]++;
                parent[Find(edge.U)] = Find(edge.V);
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                Assert.True(degree[v] is 0 or 2, $"vertex {v} must have degree 0 or 2, has {degree[v]}");
            }

            if (requireSingleComponent)
            {
                var touchedRoots = new HashSet<int>();
                for (int v = 0; v < graph.VertexCount; v++)
                {
                    if (degree[v] > 0)
                    {
                        touchedRoots.Add(Find(v));
                    }
                }

                Assert.Single(touchedRoots);
            }
        }

        /// <summary>Brute-force cycle families for a graph small enough to enumerate every edge subset.</summary>
        private static BruteForceFamily BruteForceCycles(Graph graph, bool single)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceCycles enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                if (edgeSet.Count == 0)
                {
                    continue;
                }

                if (IsCycleFamilyMember(graph, edgeSet, single))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static bool IsCycleFamilyMember(Graph graph, List<int> edgeSet, bool single)
        {
            var degree = new int[graph.VertexCount];
            var parent = Enumerable.Range(0, graph.VertexCount).ToArray();

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
                degree[edge.U]++;
                degree[edge.V]++;
                parent[Find(edge.U)] = Find(edge.V);
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (degree[v] is not (0 or 2))
                {
                    return false;
                }
            }

            if (!single)
            {
                return true;
            }

            var touchedRoots = new HashSet<int>();
            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (degree[v] > 0)
                {
                    touchedRoots.Add(Find(v));
                }
            }

            return touchedRoots.Count == 1;
        }

        private static BigInteger CountSimpleCyclesInCompleteGraphFormula(int n)
        {
            BigInteger total = BigInteger.Zero;
            for (int k = 3; k <= n; k++)
            {
                total += Binomial(n, k) * Factorial(k - 1) / 2;
            }

            return total;
        }

        private static BigInteger Binomial(int n, int k)
        {
            BigInteger result = BigInteger.One;
            for (int i = 0; i < k; i++)
            {
                result = result * (n - i) / (i + 1);
            }

            return result;
        }

        private static BigInteger Factorial(int n)
        {
            BigInteger result = BigInteger.One;
            for (int i = 2; i <= n; i++)
            {
                result *= i;
            }

            return result;
        }
    }
}

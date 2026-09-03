using System;
using System.Collections.Generic;
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
    /// M4-7 completion criteria for <see cref="ColoringSpec"/>: the count matches the chromatic polynomial
    /// closed forms for <c>Complete</c>/<c>Cycle</c>/<c>Path</c> and an independently computed
    /// deletion-contraction on small random graphs, <c>k</c> below the chromatic number builds
    /// <c>Empty</c>, <see cref="ColoringSpec.RepresentativesOnly"/>'s count divides the full count down by
    /// how many colors each class actually uses, every enumerated set is a genuine proper coloring, and
    /// <see cref="ColoringSpec.GetChild"/> does not allocate.
    /// </summary>
    public class ColoringSpecTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 3)]
        [InlineData(2, 2)]
        [InlineData(2, 4)]
        [InlineData(3, 3)]
        [InlineData(3, 5)]
        [InlineData(4, 4)]
        [InlineData(5, 5)]
        public void CompleteGraphColoringCountMatchesFallingFactorial(int n, int k)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.VertexCount * k);

            Zdd built = FrontierBuilder.Build<ColoringSpec>(manager, new ColoringSpec(graph, k));

            Assert.Equal(FallingFactorial(k, n), built.Count);
        }

        [Theory]
        [InlineData(3, 2)]
        [InlineData(3, 3)]
        [InlineData(4, 2)]
        [InlineData(4, 3)]
        [InlineData(5, 3)]
        [InlineData(6, 4)]
        public void CycleGraphColoringCountMatchesClosedForm(int n, int k)
        {
            Graph graph = Graph.Cycle(n);
            using ZddManager manager = new ZddManager(graph.VertexCount * k);

            Zdd built = FrontierBuilder.Build<ColoringSpec>(manager, new ColoringSpec(graph, k));

            BigInteger sign = n % 2 == 0 ? BigInteger.One : BigInteger.MinusOne;
            BigInteger expected = BigInteger.Pow(k - 1, n) + sign * (k - 1);
            Assert.Equal(expected, built.Count);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 3)]
        [InlineData(4, 1)]
        [InlineData(4, 2)]
        [InlineData(4, 3)]
        [InlineData(5, 3)]
        [InlineData(6, 4)]
        public void PathGraphColoringCountMatchesClosedForm(int n, int k)
        {
            Graph graph = Graph.Path(n);
            using ZddManager manager = new ZddManager(graph.VertexCount * k);

            Zdd built = FrontierBuilder.Build<ColoringSpec>(manager, new ColoringSpec(graph, k));

            BigInteger expected = BigInteger.Pow(k - 1, n - 1) * k;
            Assert.Equal(expected, built.Count);
        }

        [Theory]
        [InlineData(4, 1)]
        [InlineData(4, 2)]
        [InlineData(4, 3)]
        [InlineData(5, 2)]
        public void KBelowChromaticNumberOfCompleteGraphIsEmpty(int n, int k)
        {
            Graph graph = Graph.Complete(n);
            using ZddManager manager = new ZddManager(graph.VertexCount * k);

            Zdd built = FrontierBuilder.Build<ColoringSpec>(manager, new ColoringSpec(graph, k));

            Assert.Equal(manager.Empty, built);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void MatchesDeletionContractionOnRandomGraphs(int seed)
        {
            Graph graph = SpanningTreeSpecTests.RandomConnectedGraph(vertexCount: 5, extraEdgeProbability: 0.4, seed);

            foreach (int k in new[] { 1, 2, 3, 4 })
            {
                using ZddManager manager = new ZddManager(graph.VertexCount * k);
                Zdd built = FrontierBuilder.Build<ColoringSpec>(manager, new ColoringSpec(graph, k));

                Assert.Equal(ChromaticPolynomialByDeletionContraction(graph, k), built.Count);
            }
        }

        [Theory]
        [InlineData("path4", 3)]
        [InlineData("cycle5", 3)]
        [InlineData("grid2x3", 3)]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string graphName, int k)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount * k);

            Zdd built = FrontierBuilder.Build<ColoringSpec>(manager, new ColoringSpec(graph, k));

            BruteForceFamily expected = BruteForceColorings(graph, k, representativesOnly: false);

            FamilyAssert.AssertSameFamily($"{graphName} k={k}", built, expected);
        }

        [Fact]
        public void MatchesBruteForceEnumerationOnCompleteFour()
        {
            // K_4 with 4 colors: 16 (vertex, color) variables, small enough for 2^16 brute-force
            // enumeration to stay fast while still exercising a fully adjacent graph.
            Graph graph = Graph.Complete(4);
            const int k = 4;
            using ZddManager manager = new ZddManager(graph.VertexCount * k);

            Zdd built = FrontierBuilder.Build<ColoringSpec>(manager, new ColoringSpec(graph, k));

            BruteForceFamily expected = BruteForceColorings(graph, k, representativesOnly: false);

            FamilyAssert.AssertSameFamily("complete4 k=4", built, expected);
        }

        [Theory]
        [InlineData("path4", 3)]
        [InlineData("cycle5", 3)]
        [InlineData("grid2x3", 3)]
        public void RepresentativesOnlyMatchesBruteForceEnumeration(string graphName, int k)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount * k);

            Zdd built = FrontierBuilder.Build<ColoringSpec>(manager, new ColoringSpec(graph, k, representativesOnly: true));

            BruteForceFamily expected = BruteForceColorings(graph, k, representativesOnly: true);

            FamilyAssert.AssertSameFamily($"{graphName} k={k} representative", built, expected);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void RepresentativesOnlyCountIsFullCountDividedByKFactorialWhenEveryColorIsForced(int n)
        {
            // K_n with exactly n colors: every proper coloring is a bijection vertex -> color, so every
            // accepted coloring necessarily uses all n colors and the ratio is exactly 1/n! (see the type
            // remarks on ColoringSpec.RepresentativesOnly for why this need not hold when fewer colors
            // than K could be used).
            Graph graph = Graph.Complete(n);
            using ZddManager fullManager = new ZddManager(graph.VertexCount * n);
            using ZddManager representativeManager = new ZddManager(graph.VertexCount * n);

            Zdd full = FrontierBuilder.Build<ColoringSpec>(fullManager, new ColoringSpec(graph, n));
            Zdd representative = FrontierBuilder.Build<ColoringSpec>(
                representativeManager, new ColoringSpec(graph, n, representativesOnly: true));

            Assert.Equal(full.Count / Factorial(n), representative.Count);
            Assert.True(representative.Count > BigInteger.Zero);
        }

        [Theory]
        [InlineData("path4", 3)]
        [InlineData("cycle5", 3)]
        [InlineData("grid2x3", 3)]
        public void EveryEnumeratedSetIsAProperColoring(string graphName, int k)
        {
            Graph graph = SpanningTreeSpecTests.NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.VertexCount * k);

            Zdd built = FrontierBuilder.Build<ColoringSpec>(manager, new ColoringSpec(graph, k));

            foreach (int[] items in built.Sets())
            {
                int[]? colorOfVertex = Decode(items, graph.VertexCount, k);
                Assert.True(colorOfVertex is not null, $"{{{string.Join(",", items)}}} does not decode to one color per vertex");
                Assert.True(
                    IsProperColoring(graph, colorOfVertex!),
                    $"{{{string.Join(",", items)}}} is not a proper coloring of {graphName}");
            }
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            Assert.Throws<ArgumentNullException>(() => new ColoringSpec(null!, 3));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ConstructorRejectsNonPositiveK(int k)
        {
            Graph graph = Graph.Path(4);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColoringSpec(graph, k));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new ColoringSpec(grid, 3);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneVariablePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneVariablePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneVariablePerLevel(ColoringSpec spec, Span<int> state, int level)
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

        /// <summary>Checks the definition directly: no edge has both endpoints the same color.</summary>
        private static bool IsProperColoring(Graph graph, int[] colorOfVertex)
        {
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                Edge edge = graph.GetEdge(i);
                if (colorOfVertex[edge.U] == colorOfVertex[edge.V])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether, scanning vertices in index order, colors first appear in ascending order.</summary>
        private static bool IsRepresentative(int[] colorOfVertex)
        {
            int nextNewColor = 0;
            foreach (int color in colorOfVertex)
            {
                if (color > nextNewColor)
                {
                    return false;
                }

                if (color == nextNewColor)
                {
                    nextNewColor++;
                }
            }

            return true;
        }

        /// <summary>
        /// Decodes a <see cref="ColoringSpec"/> item set (vertex-major, color-minor: item <c>v * k + c</c>)
        /// into a per-vertex color array, or <see langword="null"/> if some vertex has zero or several colors.
        /// </summary>
        private static int[]? Decode(IReadOnlyList<int> items, int vertexCount, int k)
        {
            var colorOfVertex = new int[vertexCount];
            var seen = new bool[vertexCount];

            foreach (int item in items)
            {
                int v = item / k;
                int c = item % k;
                if (seen[v])
                {
                    return null;
                }

                seen[v] = true;
                colorOfVertex[v] = c;
            }

            for (int v = 0; v < vertexCount; v++)
            {
                if (!seen[v])
                {
                    return null;
                }
            }

            return colorOfVertex;
        }

        private static BruteForceFamily BruteForceColorings(Graph graph, int k, bool representativesOnly)
        {
            int variableCount = graph.VertexCount * k;

            if (variableCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceColorings enumerates all 2^variableCount subsets and cannot handle {variableCount} variables.",
                    nameof(graph));
            }

            var accepted = new List<int>();
            int bound = 1 << variableCount;

            for (int mask = 0; mask < bound; mask++)
            {
                var items = new List<int>();
                for (int i = 0; i < variableCount; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        items.Add(i);
                    }
                }

                int[]? colorOfVertex = Decode(items, graph.VertexCount, k);
                if (colorOfVertex is null || !IsProperColoring(graph, colorOfVertex))
                {
                    continue;
                }

                if (representativesOnly && !IsRepresentative(colorOfVertex))
                {
                    continue;
                }

                accepted.Add(mask);
            }

            return BruteForceFamily.FromMasks(variableCount, accepted);
        }

        /// <summary>The chromatic polynomial <c>P(graph, k)</c>, evaluated by deletion-contraction on one edge at a time.</summary>
        private static BigInteger ChromaticPolynomialByDeletionContraction(Graph graph, int k)
        {
            if (graph.EdgeCount == 0)
            {
                return BigInteger.Pow(k, graph.VertexCount);
            }

            Graph deleted = RemoveEdgeAt(graph, 0);
            Graph contracted = ContractEdgeAt(graph, 0);
            return ChromaticPolynomialByDeletionContraction(deleted, k)
                - ChromaticPolynomialByDeletionContraction(contracted, k);
        }

        private static Graph RemoveEdgeAt(Graph graph, int index)
        {
            var edges = new List<Edge>();
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                if (i != index)
                {
                    edges.Add(graph.GetEdge(i));
                }
            }

            return new Graph(graph.VertexCount, edges);
        }

        /// <summary>Merges the two endpoints of edge <paramref name="index"/> into one vertex, dropping the resulting self-loop and any parallel edges.</summary>
        private static Graph ContractEdgeAt(Graph graph, int index)
        {
            Edge contracted = graph.GetEdge(index);
            int keep = Math.Min(contracted.U, contracted.V);
            int drop = Math.Max(contracted.U, contracted.V);

            int Remap(int vertex) => vertex == drop ? keep : (vertex > drop ? vertex - 1 : vertex);

            var edges = new HashSet<Edge>();
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                if (i == index)
                {
                    continue;
                }

                Edge edge = graph.GetEdge(i);
                int u = Remap(edge.U);
                int v = Remap(edge.V);
                if (u == v)
                {
                    continue; // the contraction turned this edge into a self-loop
                }

                edges.Add(new Edge(u, v));
            }

            return new Graph(graph.VertexCount - 1, edges);
        }

        /// <summary><c>k * (k - 1) * ... * (k - n + 1)</c>, the chromatic polynomial of <c>Complete(n)</c>.</summary>
        private static BigInteger FallingFactorial(int k, int n)
        {
            BigInteger result = BigInteger.One;
            for (int i = 0; i < n; i++)
            {
                result *= k - i;
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

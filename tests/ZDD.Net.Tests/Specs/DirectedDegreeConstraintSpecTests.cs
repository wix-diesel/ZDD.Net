using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M7-5 completion criteria for <see cref="DirectedDegreeConstraintSpec"/>: matches brute-force
    /// enumeration on small directed graphs (vertex count ≤ 8) with every enumerated set's in-/out-degrees
    /// checked directly against the bounds, reproduces <see cref="DirectedPathSpec"/>'s degree profile as a
    /// (non-equal) superset once connectivity is dropped — the directed analogue of M3-7's own containment
    /// check for <see cref="DegreeConstraintSpec"/> — an unsatisfiable bound builds to <c>Empty</c>, invalid
    /// constructor arguments throw, and <c>GetChild</c> does not allocate.
    /// </summary>
    public class DirectedDegreeConstraintSpecTests
    {
        [Theory]
        [InlineData("onePath4varied")]
        [InlineData("complete4varied")]
        [InlineData("bidirectedTriangleVaried")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string caseName)
        {
            (DirectedGraph graph, int[] inLo, int[] inHi, int[] outLo, int[] outHi) = NamedCase(caseName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedDegreeConstraintSpec>(
                manager, new DirectedDegreeConstraintSpec(graph, inLo, inHi, outLo, outHi));
            BruteForceFamily expected = BruteForceDirectedDegreeConstraint(graph, inLo, inHi, outLo, outHi);

            FamilyAssert.AssertSameFamily(caseName, built, expected);

            foreach (int[] arcSet in built.Sets())
            {
                AssertDegreesWithinBounds(graph, arcSet, inLo, inHi, outLo, outHi);
            }
        }

        [Theory]
        [InlineData(5, 8, 1)]
        [InlineData(6, 10, 2)]
        [InlineData(8, 14, 3)]
        public void MatchesBruteForceEnumerationOnRandomDirectedGraphs(int vertexCount, int arcCount, int seed)
        {
            DirectedGraph graph = RandomDirectedGraph(vertexCount, arcCount, seed);
            var random = new Random(seed * 97);
            var inLo = new int[vertexCount];
            var inHi = new int[vertexCount];
            var outLo = new int[vertexCount];
            var outHi = new int[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                inHi[v] = graph.InDegree(v);
                outHi[v] = graph.OutDegree(v);
                inLo[v] = inHi[v] == 0 ? 0 : random.Next(inHi[v] + 1);
                outLo[v] = outHi[v] == 0 ? 0 : random.Next(outHi[v] + 1);
            }

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd built = FrontierBuilder.Build<DirectedDegreeConstraintSpec>(
                manager, new DirectedDegreeConstraintSpec(graph, inLo, inHi, outLo, outHi));
            BruteForceFamily expected = BruteForceDirectedDegreeConstraint(graph, inLo, inHi, outLo, outHi);

            FamilyAssert.AssertSameFamily($"n={vertexCount} arcs={arcCount} seed={seed}", built, expected);
        }

        [Theory]
        [InlineData("bidirectedPath4", 0, 3)]
        [InlineData("bidirectedTriangle", 0, 2)]
        [InlineData("complete4", 0, 3)]
        public void ReproducesDirectedPathSpecDegreeProfileAsASuperset(string graphName, int from, int to)
        {
            // M3-7's own precedent for DegreeConstraintSpec ([0, 2] containing CycleSpec's family once
            // connectivity is dropped): fixing from's degree profile to (out 1, in 0), to's to (out 0, in 1),
            // and every other vertex to (out 0..1, in 0..1) accepts every directed simple path from `from` to
            // `to`, plus (on graphs with enough spare vertices) anything else sharing that local degree shape
            // but no connectivity or acyclicity guarantee — containment, not necessarily strict on every
            // graph (see AtLeastOneCaseIsAStrictSuperset for a graph roomy enough to show the extra members).
            DirectedGraph graph = DirectedGraphFor(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd paths = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, from, to));

            var inLo = new int[graph.VertexCount];
            var inHi = new int[graph.VertexCount];
            var outLo = new int[graph.VertexCount];
            var outHi = new int[graph.VertexCount];
            for (int v = 0; v < graph.VertexCount; v++)
            {
                if (v == from)
                {
                    outLo[v] = outHi[v] = 1;
                    inLo[v] = inHi[v] = 0;
                }
                else if (v == to)
                {
                    outLo[v] = outHi[v] = 0;
                    inLo[v] = inHi[v] = 1;
                }
                else
                {
                    outLo[v] = 0;
                    outHi[v] = 1;
                    inLo[v] = 0;
                    inHi[v] = 1;
                }
            }

            Zdd degree = FrontierBuilder.Build<DirectedDegreeConstraintSpec>(
                manager, new DirectedDegreeConstraintSpec(graph, inLo, inHi, outLo, outHi));

            Assert.Equal(manager.Empty, paths.Difference(degree)); // every path is a degree-profile member

            foreach (int[] arcSet in degree.Sets())
            {
                AssertDegreesWithinBounds(graph, arcSet, inLo, inHi, outLo, outHi);
            }
        }

        [Fact]
        public void AtLeastOneCaseIsAStrictSuperset()
        {
            // complete4 has two spare vertices besides 0 (from) and 3 (to), leaving room for degree-profile
            // members that are not an actual path (e.g. one that also closes a disjoint cycle on the rest) —
            // unlike the 3-vertex triangle case above, where every degree-profile member happens to be a
            // real path simply because there is no room for anything else.
            DirectedGraph graph = DirectedGraphFor("complete4");
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd paths = FrontierBuilder.Build<DirectedPathSpec>(manager, new DirectedPathSpec(graph, from: 0, to: 3));

            var inLo = new[] { 0, 0, 0, 1 };
            var inHi = new[] { 0, 1, 1, 1 };
            var outLo = new[] { 1, 0, 0, 0 };
            var outHi = new[] { 1, 1, 1, 0 };

            Zdd degree = FrontierBuilder.Build<DirectedDegreeConstraintSpec>(
                manager, new DirectedDegreeConstraintSpec(graph, inLo, inHi, outLo, outHi));

            Assert.Equal(manager.Empty, paths.Difference(degree));
            Assert.NotEqual(paths, degree);
        }

        [Fact]
        public void ConstructorRejectsNullArguments()
        {
            DirectedGraph graph = DirectedGraph.Path(2);
            var zero = new[] { 0, 0 };
            var one = new[] { 1, 1 };

            Assert.Throws<ArgumentNullException>(() => new DirectedDegreeConstraintSpec(null!, zero, one, zero, one));
            Assert.Throws<ArgumentNullException>(() => new DirectedDegreeConstraintSpec(graph, null!, one, zero, one));
            Assert.Throws<ArgumentNullException>(() => new DirectedDegreeConstraintSpec(graph, zero, null!, zero, one));
            Assert.Throws<ArgumentNullException>(() => new DirectedDegreeConstraintSpec(graph, zero, one, null!, one));
            Assert.Throws<ArgumentNullException>(() => new DirectedDegreeConstraintSpec(graph, zero, one, zero, null!));
            Assert.Throws<ArgumentNullException>(() => new DirectedDegreeConstraintSpec(null!, 0, 1, 0, 1));
        }

        [Fact]
        public void ConstructorRejectsWrongLengthArrays()
        {
            DirectedGraph graph = DirectedGraph.Path(4); // 4 vertices
            var wrong = new[] { 0, 0, 0 };
            var right = new[] { 1, 1, 1, 1 };

            Assert.Throws<ArgumentException>(() => new DirectedDegreeConstraintSpec(graph, wrong, right, right, right));
            Assert.Throws<ArgumentException>(() => new DirectedDegreeConstraintSpec(graph, right, wrong, right, right));
            Assert.Throws<ArgumentException>(() => new DirectedDegreeConstraintSpec(graph, right, right, wrong, right));
            Assert.Throws<ArgumentException>(() => new DirectedDegreeConstraintSpec(graph, right, right, right, wrong));
        }

        [Fact]
        public void ConstructorRejectsNegativeLo()
        {
            DirectedGraph graph = DirectedGraph.Path(3);
            var ok = new[] { 0, 0, 0 };
            var negative = new[] { 0, -1, 0 };

            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectedDegreeConstraintSpec(graph, negative, ok, ok, ok));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectedDegreeConstraintSpec(graph, ok, ok, negative, ok));
        }

        [Fact]
        public void ConstructorRejectsHiBelowLo()
        {
            DirectedGraph graph = DirectedGraph.Path(3);
            var lo = new[] { 0, 2, 0 };
            var hi = new[] { 1, 1, 1 };
            var zero = new[] { 0, 0, 0 };

            Assert.Throws<ArgumentException>(() => new DirectedDegreeConstraintSpec(graph, lo, hi, zero, zero));
            Assert.Throws<ArgumentException>(() => new DirectedDegreeConstraintSpec(graph, zero, zero, lo, hi));
        }

        [Fact]
        public void UnsatisfiableOutLoBuildsToEmpty()
        {
            // A one-way path: every vertex's out-degree is at most 1. Requiring outLo = 2 everywhere is impossible.
            DirectedGraph graph = DirectedGraph.Path(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedDegreeConstraintSpec>(
                manager, new DirectedDegreeConstraintSpec(graph, inLo: 0, inHi: 1, outLo: 2, outHi: 2));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void VertexWithNoIncomingArcsAndPositiveInLoIsEmpty()
        {
            // Vertex 0 of a one-way path never has an incoming arc.
            DirectedGraph graph = DirectedGraph.Path(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DirectedDegreeConstraintSpec>(
                manager, new DirectedDegreeConstraintSpec(graph, inLo: 1, inHi: 1, outLo: 0, outHi: 1));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            DirectedGraph graph = DirectedGraph.Bidirected(Graph.Grid(4, 4));
            var spec = new DirectedDegreeConstraintSpec(graph, inLo: 0, inHi: 2, outLo: 0, outHi: 2);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(DirectedDegreeConstraintSpec spec, Span<int> state, int level)
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

        private static void AssertDegreesWithinBounds(DirectedGraph graph, int[] arcSet, int[] inLo, int[] inHi, int[] outLo, int[] outHi)
        {
            var inDegree = new int[graph.VertexCount];
            var outDegree = new int[graph.VertexCount];
            foreach (int edgeIndex in arcSet)
            {
                DirectedEdge arc = graph.GetEdge(edgeIndex);
                outDegree[arc.From]++;
                inDegree[arc.To]++;
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                Assert.True(
                    inDegree[v] >= inLo[v] && inDegree[v] <= inHi[v],
                    $"vertex {v} has in-degree {inDegree[v]}, expected [{inLo[v]}, {inHi[v]}]");
                Assert.True(
                    outDegree[v] >= outLo[v] && outDegree[v] <= outHi[v],
                    $"vertex {v} has out-degree {outDegree[v]}, expected [{outLo[v]}, {outHi[v]}]");
            }
        }

        private static BruteForceFamily BruteForceDirectedDegreeConstraint(DirectedGraph graph, int[] inLo, int[] inHi, int[] outLo, int[] outHi)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceDirectedDegreeConstraint enumerates all 2^edgeCount subsets and cannot handle {edgeCount} arcs.",
                    nameof(graph));
            }

            int bound = 1 << edgeCount;

            for (int mask = 0; mask < bound; mask++)
            {
                var inDegree = new int[graph.VertexCount];
                var outDegree = new int[graph.VertexCount];
                for (int i = 0; i < edgeCount; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        DirectedEdge arc = graph.GetEdge(i);
                        outDegree[arc.From]++;
                        inDegree[arc.To]++;
                    }
                }

                bool withinBounds = true;
                for (int v = 0; v < graph.VertexCount; v++)
                {
                    if (inDegree[v] < inLo[v] || inDegree[v] > inHi[v] || outDegree[v] < outLo[v] || outDegree[v] > outHi[v])
                    {
                        withinBounds = false;
                        break;
                    }
                }

                if (withinBounds)
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        private static DirectedGraph DirectedGraphFor(string graphName) => graphName switch
        {
            "bidirectedPath4" => DirectedGraph.Bidirected(Graph.Path(4)),
            "bidirectedTriangle" => DirectedGraph.Bidirected(Graph.Cycle(3)),
            "complete4" => DirectedGraph.Complete(4),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static (DirectedGraph Graph, int[] InLo, int[] InHi, int[] OutLo, int[] OutHi) NamedCase(string caseName) => caseName switch
        {
            "onePath4varied" => (DirectedGraph.Path(4), new[] { 0, 0, 1, 0 }, new[] { 0, 1, 1, 1 }, new[] { 0, 1, 0, 0 }, new[] { 1, 1, 1, 0 }),
            "complete4varied" => (DirectedGraph.Complete(4), new[] { 0, 1, 0, 2 }, new[] { 1, 2, 3, 3 }, new[] { 1, 0, 0, 1 }, new[] { 3, 2, 2, 3 }),
            "bidirectedTriangleVaried" => (DirectedGraph.Bidirected(Graph.Cycle(3)), new[] { 0, 0, 1 }, new[] { 1, 2, 2 }, new[] { 0, 1, 0 }, new[] { 2, 2, 1 }),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };

        /// <summary>Builds a random directed graph over <paramref name="arcCount"/> distinct ordered pairs of distinct vertices.</summary>
        private static DirectedGraph RandomDirectedGraph(int vertexCount, int arcCount, int seed)
        {
            var random = new Random(seed);
            var candidates = new List<DirectedEdge>();
            for (int u = 0; u < vertexCount; u++)
            {
                for (int v = 0; v < vertexCount; v++)
                {
                    if (u != v)
                    {
                        candidates.Add(new DirectedEdge(u, v));
                    }
                }
            }

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            return new DirectedGraph(vertexCount, candidates.Take(Math.Min(arcCount, candidates.Count)));
        }
    }
}

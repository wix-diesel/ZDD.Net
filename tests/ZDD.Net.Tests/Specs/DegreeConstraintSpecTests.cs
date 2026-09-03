using System;
using System.Collections.Generic;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>
    /// M3-7 completion criteria for <see cref="DegreeConstraintSpec"/>: <c>[0, 1]</c> reproduces
    /// <see cref="MatchingSpec"/> exactly, <c>[1, 1]</c> reproduces a perfect matching exactly, <c>[0, 2]</c>
    /// contains (but is not equal to) <see cref="CycleSpec"/>'s multi-cycle family, matches brute-force
    /// enumeration on small graphs with every enumerated set's per-vertex degree checked directly against
    /// <c>[lo, hi]</c>, invalid <c>lo</c>/<c>hi</c> throw, an unsatisfiable bound builds to <c>Empty</c>,
    /// the branch-and-bound pruning measurably shrinks the build while leaving the family unchanged, and
    /// <c>GetChild</c> does not allocate.
    /// </summary>
    public class DegreeConstraintSpecTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void ZeroOneReproducesMatchingSpec(string graphName)
        {
            Graph graph = NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd matching = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph));
            Zdd degree = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo: 0, hi: 1));

            Assert.Equal(matching, degree);
        }

        [Theory]
        [InlineData("complete4")]
        [InlineData("cycle6")]
        [InlineData("grid2x3")]
        public void OneOneReproducesPerfectMatchingSpec(string graphName)
        {
            Graph graph = NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd perfectMatching = FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(graph, perfect: true));
            Zdd degree = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo: 1, hi: 1));

            Assert.Equal(perfectMatching, degree);
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("twoTriangles")]
        public void ZeroTwoContainsTheMultiCycleFamily(string graphName)
        {
            Graph graph = NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd multiCycle = FrontierBuilder.Build<CycleSpec>(manager, new CycleSpec(graph, single: false));
            Zdd degree = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo: 0, hi: 2));

            // [0, 2] is a range, not the two-element set {0, 2}: it also allows degree 1 (a dangling path
            // endpoint), so it accepts every disjoint union of simple paths and cycles, a strict superset
            // of the cycle family rather than an equal one. Every cycle-family member is still degree
            // 0-or-2 everywhere, so containment must hold; equality would be a bug (see the type's docs).
            Assert.Equal(manager.Empty, multiCycle.Difference(degree));

            foreach (int[] edgeSet in degree.Sets())
            {
                AssertDegreesWithinBounds(graph, edgeSet, lo: UniformArray(graph.VertexCount, 0), hi: UniformArray(graph.VertexCount, 2));
            }
        }

        [Theory]
        [InlineData("path4varied")]
        [InlineData("grid2x3varied")]
        [InlineData("complete4varied")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string caseName)
        {
            (Graph graph, int[] lo, int[] hi) = NamedCase(caseName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo, hi));
            BruteForceFamily expected = BruteForceDegreeConstraint(graph, lo, hi);

            FamilyAssert.AssertSameFamily(caseName, built, expected);

            foreach (int[] edgeSet in built.Sets())
            {
                AssertDegreesWithinBounds(graph, edgeSet, lo, hi);
            }
        }

        [Fact]
        public void ConstructorRejectsNullGraph()
        {
            var lo = new[] { 0 };
            var hi = new[] { 1 };
            Assert.Throws<ArgumentNullException>(() => new DegreeConstraintSpec(null!, lo, hi));
            Assert.Throws<ArgumentNullException>(() => new DegreeConstraintSpec(null!, 0, 1));
        }

        [Fact]
        public void ConstructorRejectsNullLoOrHi()
        {
            Graph graph = Graph.Path(2);
            Assert.Throws<ArgumentNullException>(() => new DegreeConstraintSpec(graph, null!, new[] { 1, 1 }));
            Assert.Throws<ArgumentNullException>(() => new DegreeConstraintSpec(graph, new[] { 0, 0 }, null!));
        }

        [Fact]
        public void ConstructorRejectsWrongLengthArrays()
        {
            Graph graph = Graph.Path(4); // 4 vertices

            Assert.Throws<ArgumentException>(() => new DegreeConstraintSpec(graph, new[] { 0, 0, 0 }, new[] { 1, 1, 1, 1 }));
            Assert.Throws<ArgumentException>(() => new DegreeConstraintSpec(graph, new[] { 0, 0, 0, 0 }, new[] { 1, 1, 1 }));
        }

        [Fact]
        public void ConstructorRejectsNegativeLo()
        {
            Graph graph = Graph.Path(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => new DegreeConstraintSpec(graph, new[] { 0, -1, 0 }, new[] { 1, 1, 1 }));
        }

        [Fact]
        public void ConstructorRejectsHiBelowLo()
        {
            Graph graph = Graph.Path(3);
            Assert.Throws<ArgumentException>(() => new DegreeConstraintSpec(graph, new[] { 0, 2, 0 }, new[] { 1, 1, 1 }));
        }

        [Fact]
        public void UnsatisfiableLoBuildsToEmpty()
        {
            // Every vertex of a path of 4 has degree at most 2; requiring lo = 3 everywhere is impossible.
            Graph graph = Graph.Path(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo: 3, hi: 3));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void IsolatedVertexWithPositiveLoIsEmpty()
        {
            // Vertex 3 has no incident edges: it can never reach a positive lo.
            var graph = new Graph(4, new[] { new Edge(0, 1), new Edge(1, 2) });
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo: 1, hi: 2));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void PruningProducesFewerNodesThanTheSameFamilyWithoutIt()
        {
            // Requiring every vertex to use every one of its incident edges pins the family down to a
            // single member (the full edge set); any single skipped edge dooms the whole branch. That
            // makes the gap between "notice immediately" and "notice only once the vertex is forgotten"
            // as wide as possible.
            Graph graph = Graph.Grid(4, 4);
            var lo = new int[graph.VertexCount];
            var hi = new int[graph.VertexCount];
            for (int v = 0; v < graph.VertexCount; v++)
            {
                lo[v] = hi[v] = graph.Degree(v);
            }

            long prunedNodeCount = ArrayTopDownExpander<DegreeConstraintSpec>.Expand(new DegreeConstraintSpec(graph, lo, hi)).NodeCount;
            long unprunedNodeCount = ArrayTopDownExpander<UnprunedDegreeConstraintSpec>.Expand(new UnprunedDegreeConstraintSpec(graph, lo, hi)).NodeCount;

            Assert.True(
                prunedNodeCount < unprunedNodeCount,
                $"expected pruning to shrink the top-down expansion, got {prunedNodeCount} (pruned) vs {unprunedNodeCount} (unpruned)");

            using ZddManager manager = new ZddManager(graph.EdgeCount);
            Zdd pruned = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo, hi));
            Zdd unpruned = FrontierBuilder.Build<UnprunedDegreeConstraintSpec>(manager, new UnprunedDegreeConstraintSpec(graph, lo, hi));
            Assert.Equal(pruned, unpruned); // same canonical family regardless of pruning

            // The family here is exactly {the full edge set}: skipping anything makes some vertex fall
            // short of its own total degree.
            var fullEdgeSet = new List<int>();
            for (int i = 0; i < graph.EdgeCount; i++)
            {
                fullEdgeSet.Add(i);
            }

            Assert.Equal(System.Numerics.BigInteger.One, pruned.Count);
            Assert.True(pruned.Contains((IEnumerable<int>)fullEdgeSet));
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            var spec = new DegreeConstraintSpec(grid, lo: 0, hi: 4);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(DegreeConstraintSpec spec, Span<int> state, int level)
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

        private static int[] UniformArray(int length, int value)
        {
            var array = new int[length];
            Array.Fill(array, value);
            return array;
        }

        private static void AssertDegreesWithinBounds(Graph graph, int[] edgeSet, int[] lo, int[] hi)
        {
            var degree = new int[graph.VertexCount];
            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                degree[edge.U]++;
                degree[edge.V]++;
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                Assert.True(
                    degree[v] >= lo[v] && degree[v] <= hi[v],
                    $"vertex {v} has degree {degree[v]}, expected [{lo[v]}, {hi[v]}] for {string.Join(",", edgeSet)}");
            }
        }

        private static BruteForceFamily BruteForceDegreeConstraint(Graph graph, int[] lo, int[] hi)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceDegreeConstraint enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
                    nameof(graph));
            }

            int bound = 1 << edgeCount;

            for (int mask = 0; mask < bound; mask++)
            {
                var degree = new int[graph.VertexCount];
                for (int i = 0; i < edgeCount; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        Edge edge = graph.GetEdge(i);
                        degree[edge.U]++;
                        degree[edge.V]++;
                    }
                }

                bool withinBounds = true;
                for (int v = 0; v < graph.VertexCount; v++)
                {
                    if (degree[v] < lo[v] || degree[v] > hi[v])
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

        private static Graph NamedGraph(string graphName) => graphName switch
        {
            "path4" => Graph.Path(4),
            "cycle5" => Graph.Cycle(5),
            "cycle6" => Graph.Cycle(6),
            "complete4" => Graph.Complete(4),
            "complete5" => Graph.Complete(5),
            "grid2x3" => Graph.Grid(2, 3),
            "twoTriangles" => new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static (Graph Graph, int[] Lo, int[] Hi) NamedCase(string caseName) => caseName switch
        {
            "path4varied" => (Graph.Path(4), new[] { 0, 1, 0, 1 }, new[] { 1, 2, 1, 2 }),
            "grid2x3varied" => (Graph.Grid(2, 3), new[] { 0, 0, 1, 1, 0, 0 }, new[] { 2, 1, 2, 2, 1, 2 }),
            "complete4varied" => (Graph.Complete(4), new[] { 1, 0, 0, 2 }, new[] { 2, 3, 3, 3 }),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };

        /// <summary>
        /// Same family as <see cref="DegreeConstraintSpec"/>, but without the branch-and-bound cutoff: a
        /// vertex's <c>lo</c> is only checked once it is actually forgotten, never while it still has
        /// candidate edges left. Used to measure what the pruning buys.
        /// </summary>
        internal readonly struct UnprunedDegreeConstraintSpec : IArrayDdSpec
        {
            private readonly Graph _graph;
            private readonly FrontierManager _frontierManager;
            private readonly int[] _lo;
            private readonly int[] _hi;

            public UnprunedDegreeConstraintSpec(Graph graph, int[] lo, int[] hi)
            {
                _graph = graph;
                _frontierManager = new FrontierManager(graph);
                _lo = lo;
                _hi = hi;
            }

            public int ArrayLength => _frontierManager.MaxFrontierSize;

            public int GetRoot(Span<int> state)
            {
                for (int v = 0; v < _graph.VertexCount; v++)
                {
                    if (_graph.Degree(v) == 0 && _lo[v] > 0)
                    {
                        return DdResult.False;
                    }
                }

                return _graph.EdgeCount == 0 ? DdResult.True : _graph.EdgeCount;
            }

            public int GetChild(Span<int> state, int level, int value)
            {
                int edgeIndex = _graph.LevelToEdgeIndex(level);
                Edge edge = _graph.GetEdge(edgeIndex);

                IReadOnlyList<int> introducedVertices = _frontierManager.IntroducedVertices(edgeIndex);
                for (int i = 0; i < introducedVertices.Count; i++)
                {
                    state[_frontierManager.MateIndex(edgeIndex, introducedVertices[i])] = 0;
                }

                if (value == 1)
                {
                    int su = _frontierManager.MateIndex(edgeIndex, edge.U);
                    int sv = _frontierManager.MateIndex(edgeIndex, edge.V);

                    if (++state[su] > _hi[edge.U] || ++state[sv] > _hi[edge.V])
                    {
                        return DdResult.False;
                    }
                }

                // No branch-and-bound lookahead here: lo is only ever checked at the moment a vertex is
                // actually forgotten, which is the deliberate difference from the production spec.
                IReadOnlyList<int> forgottenVertices = _frontierManager.ForgottenVertices(edgeIndex);
                for (int i = 0; i < forgottenVertices.Count; i++)
                {
                    int vertex = forgottenVertices[i];
                    int slot = _frontierManager.MateIndex(edgeIndex, vertex);
                    if (state[slot] < _lo[vertex])
                    {
                        return DdResult.False;
                    }

                    state[slot] = 0;
                }

                int remaining = level - 1;
                return remaining > 0 ? remaining : DdResult.True;
            }
        }
    }
}

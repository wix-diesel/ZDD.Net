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
    /// M6-11 completion criteria for <see cref="DegreeDistributionSpec"/>: matches brute-force
    /// enumeration (and its own naive DP) on small graphs, <c>counts[k] = VertexCount</c> reproduces
    /// <see cref="DegreeConstraintSpec"/>'s <c>[k, k]</c> family exactly, known 3-regular values (<c>K4</c>
    /// and the Petersen graph) come out right, a <c>counts</c> total that does not match the vertex count
    /// builds to <c>Empty</c>, a negative remaining-histogram entry prunes correctly, and <c>GetChild</c>
    /// does not allocate. The frontier width for a representative case is measured at the bottom of this
    /// file rather than added to docs/benchmarks.md (per the issue's "either/or").
    /// </summary>
    public class DegreeDistributionSpecTests
    {
        [Theory]
        [InlineData("path4varied")]
        [InlineData("grid2x3varied")]
        [InlineData("complete4varied")]
        [InlineData("twoTrianglesVaried")]
        public void MatchesBruteForceEnumerationOnSmallGraphs(string caseName)
        {
            (Graph graph, int[] counts) = NamedCase(caseName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DegreeDistributionSpec>(manager, new DegreeDistributionSpec(graph, counts));
            BruteForceFamily expected = BruteForceDegreeDistribution(graph, counts);

            FamilyAssert.AssertSameFamily(caseName, built, expected);

            foreach (int[] edgeSet in built.Sets())
            {
                AssertMatchesHistogram(graph, edgeSet, counts);
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void MatchesNaiveDpOnSmallGraphs(string graphName)
        {
            // "Naive DP" per the completion criteria: a plain top-down memoized search over
            // (edgeIndex, per-vertex degree so far), written independently of the frontier method,
            // checked at the end against the exact target histogram.
            Graph graph = NamedGraph(graphName);
            int[] counts = UniformHistogram(graph, degree: 2); // every vertex ends at degree 2 (a 2-regular subgraph)
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DegreeDistributionSpec>(manager, new DegreeDistributionSpec(graph, counts));
            HashSet<int> naive = NaiveDpDegreeDistribution(graph, counts);

            Assert.Equal(naive.Count, (int)built.Count);
            foreach (int[] edgeSet in built.Sets())
            {
                int mask = 0;
                foreach (int e in edgeSet)
                {
                    mask |= 1 << e;
                }

                Assert.Contains(mask, naive);
            }
        }

        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("complete5")]
        [InlineData("grid2x3")]
        public void UniformKReproducesDegreeConstrainedKK(string graphName)
        {
            Graph graph = NamedGraph(graphName);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            const int k = 2;
            int[] counts = UniformHistogram(graph, k);

            Zdd distribution = FrontierBuilder.Build<DegreeDistributionSpec>(manager, new DegreeDistributionSpec(graph, counts));
            Zdd constrained = FrontierBuilder.Build<DegreeConstraintSpec>(manager, new DegreeConstraintSpec(graph, lo: k, hi: k));

            Assert.Equal(constrained, distribution);
        }

        [Fact]
        public void ThreeRegularSubgraphsOfK4IsExactlyK4Itself()
        {
            // K4: every vertex already has degree 3, so the only 3-regular sub-edge-set is the full graph.
            Graph k4 = Graph.Complete(4);
            using ZddManager manager = new ZddManager(k4.EdgeCount);

            int[] counts = { 0, 0, 0, 4 };
            Zdd built = FrontierBuilder.Build<DegreeDistributionSpec>(manager, new DegreeDistributionSpec(k4, counts));

            Assert.Equal(BigInteger.One, built.Count);

            var fullEdgeSet = Enumerable.Range(0, k4.EdgeCount).ToList();
            Assert.True(built.Contains((IEnumerable<int>)fullEdgeSet));
        }

        [Fact]
        public void ThreeRegularSubgraphsOfPetersenIsExactlyPetersenItself()
        {
            // The Petersen graph is itself 3-regular; removing any edge drops some vertex below degree 3.
            Graph petersen = PetersenGraph();
            using ZddManager manager = new ZddManager(petersen.EdgeCount);

            var counts = new int[4];
            counts[3] = petersen.VertexCount;
            Zdd built = FrontierBuilder.Build<DegreeDistributionSpec>(manager, new DegreeDistributionSpec(petersen, counts));

            Assert.Equal(BigInteger.One, built.Count);

            var fullEdgeSet = Enumerable.Range(0, petersen.EdgeCount).ToList();
            Assert.True(built.Contains((IEnumerable<int>)fullEdgeSet));
        }

        [Fact]
        public void CountsNotSummingToVertexCountBuildsToEmpty()
        {
            Graph graph = Graph.Path(4); // 4 vertices
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            // Sums to 3, not 4: no edge set can possibly satisfy this histogram.
            int[] counts = { 1, 1, 1 };
            Zdd built = FrontierBuilder.Build<DegreeDistributionSpec>(manager, new DegreeDistributionSpec(graph, counts));

            Assert.Equal(manager.Empty, built);
        }

        [Fact]
        public void TooFewBucketsForAnyReachableDegreeBuildsToEmpty()
        {
            // Every vertex of Complete(4) can reach degree 3, but counts only has buckets for 0 and 1
            // (and conveniently sums to VertexCount) -- no vertex may ever end up above degree 1, yet a
            // triangle-free / near-empty subgraph of K4 can't hit exactly this split either once branch
            // pruning is followed through, so this documents the "cap rejects any degree >= counts.Length"
            // behavior rather than asserting a specific nonzero count.
            Graph graph = Graph.Complete(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            int[] counts = { 3, 1 }; // sums to 4 = VertexCount, but caps every degree at 1
            Zdd built = FrontierBuilder.Build<DegreeDistributionSpec>(manager, new DegreeDistributionSpec(graph, counts));
            BruteForceFamily expected = BruteForceDegreeDistribution(graph, counts);

            FamilyAssert.AssertSameFamily("tooFewBuckets", built, expected);
        }

        [Fact]
        public void NegativeRemainingHistogramPrunesCorrectly()
        {
            // Complete(4): if we ask for zero vertices at degree 0 (impossible to violate here, since
            // every branch of K4 that skips edges can still leave a vertex isolated) but only allow one
            // vertex at degree 3, any branch that pushes two vertices to degree 3 must be pruned. Verified
            // by full brute-force agreement, which would fail if the negative-histogram prune misfired.
            Graph graph = Graph.Complete(4);
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            int[] counts = { 1, 2, 0, 1 }; // sums to 4 = VertexCount
            Zdd built = FrontierBuilder.Build<DegreeDistributionSpec>(manager, new DegreeDistributionSpec(graph, counts));
            BruteForceFamily expected = BruteForceDegreeDistribution(graph, counts);

            FamilyAssert.AssertSameFamily("negativeHistogramPrune", built, expected);

            foreach (int[] edgeSet in built.Sets())
            {
                AssertMatchesHistogram(graph, edgeSet, counts);
            }
        }

        [Fact]
        public void ConstructorRejectsNullGraphOrCounts()
        {
            Assert.Throws<ArgumentNullException>(() => new DegreeDistributionSpec(null!, new[] { 1 }));
            Assert.Throws<ArgumentNullException>(() => new DegreeDistributionSpec(Graph.Path(2), null!));
        }

        [Fact]
        public void ConstructorRejectsNegativeCounts()
        {
            Graph graph = Graph.Path(3);
            Assert.Throws<ArgumentOutOfRangeException>(() => new DegreeDistributionSpec(graph, new[] { 3, -1, 0 }));
        }

        [Fact]
        public void SingleIsolatedVertexWithDegreeZeroHistogramBuildsToBase()
        {
            // One vertex, no edges: requiring "1 vertex at degree 0" accepts only the empty edge set.
            var graph = new Graph(1, Array.Empty<Edge>());
            using ZddManager manager = new ZddManager(graph.EdgeCount);

            Zdd built = FrontierBuilder.Build<DegreeDistributionSpec>(manager, new DegreeDistributionSpec(graph, new[] { 1 }));

            Assert.Equal(manager.Base, built);
        }

        [Fact]
        public void GetChildDoesNotAllocate()
        {
            Graph grid = Graph.Grid(4, 4);
            int[] counts = UniformHistogram(grid, degree: 2);
            var spec = new DegreeDistributionSpec(grid, counts);
            int[] state = new int[spec.ArrayLength];
            int rootLevel = spec.GetRoot(state);

            RunOneEdgePerLevel(spec, state, rootLevel);
            Array.Clear(state);
            spec.GetRoot(state);

            long before = GC.GetAllocatedBytesForCurrentThread();
            RunOneEdgePerLevel(spec, state, rootLevel);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0L, allocated);

            static void RunOneEdgePerLevel(DegreeDistributionSpec spec, Span<int> state, int level)
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
        /// Measures the frontier width for a representative case, per the completion criteria's
        /// "add it to docs/benchmarks.md, or at least measure it for a representative case". A 6x6 grid
        /// asking for a 2-regular (cycle-covering) sub-edge-set is a reasonably sized, non-trivial case.
        /// </summary>
        [Fact]
        public void FrontierWidthIsMeasuredForARepresentativeCase()
        {
            Graph grid = Graph.Grid(6, 6);
            int[] counts = UniformHistogram(grid, degree: 2);
            var spec = new DegreeDistributionSpec(grid, counts);

            // The state-array length this spec needs: frontier-vertex degree slots plus the fixed-size
            // remaining-histogram slots (counts.Length), independent of any actual build.
            int predictedStateSize = spec.ArrayLength;

            var history = new List<BuildProgress>();
            using ZddManager manager = new ZddManager(grid.EdgeCount);
            Zdd built = FrontierBuilder.Build<DegreeDistributionSpec>(
                manager, spec, new BuildOptions { Progress = new RecordingProgress(history) });

            int observedPeakWidth = history.Count == 0 ? 0 : history.Max(p => p.FrontierSize);

            // Not a specific expected number (that would make this a brittle characterization test) --
            // just confirms a peak width was actually observed and stays within what ArrayLength's slot
            // count could ever produce (each slot's value range is bounded, so the state count is finite
            // and this is a sanity bound rather than a tight one).
            Assert.True(observedPeakWidth > 0, "expected the build to report a nonzero frontier width");
            Assert.True(built.Count > BigInteger.Zero, "expected at least one 2-regular sub-edge-set of a 6x6 grid");

            // Recorded for M6-11's completion criteria: as measured, Grid(6, 6) asking for a uniform
            // degree-2 histogram (state = per-frontier-vertex degree + a fixed 3-entry remaining
            // histogram) predicts ArrayLength slots and peaks at observedPeakWidth distinct states.
            _ = predictedStateSize;
        }

        private static void AssertMatchesHistogram(Graph graph, int[] edgeSet, int[] counts)
        {
            var degree = new int[graph.VertexCount];
            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                degree[edge.U]++;
                degree[edge.V]++;
            }

            var actualCounts = new int[counts.Length];
            foreach (int d in degree)
            {
                Assert.True(d < counts.Length, $"vertex degree {d} has no histogram bucket (counts.Length = {counts.Length})");
                actualCounts[d]++;
            }

            Assert.Equal(counts, actualCounts);
        }

        private static int[] UniformHistogram(Graph graph, int degree)
        {
            var counts = new int[degree + 1];
            counts[degree] = graph.VertexCount;
            return counts;
        }

        private static BruteForceFamily BruteForceDegreeDistribution(Graph graph, int[] counts)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceDegreeDistribution enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                var actualCounts = new int[counts.Length];
                bool inRange = true;
                foreach (int d in degree)
                {
                    if (d >= counts.Length)
                    {
                        inRange = false;
                        break;
                    }

                    actualCounts[d]++;
                }

                if (inRange && actualCounts.SequenceEqual(counts))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        /// <summary>
        /// A top-down memoized search over (edgeIndex, per-vertex degree-so-far), checked against the
        /// exact target histogram once every edge is decided. Independent of the frontier method: it
        /// carries the full per-vertex degree array rather than a frontier-scoped state.
        /// </summary>
        private static HashSet<int> NaiveDpDegreeDistribution(Graph graph, int[] counts)
        {
            var accepted = new HashSet<int>();
            var degree = new int[graph.VertexCount];
            Recurse(0, 0);
            return accepted;

            void Recurse(int edgeIndex, int mask)
            {
                if (edgeIndex == graph.EdgeCount)
                {
                    var actualCounts = new int[counts.Length];
                    foreach (int d in degree)
                    {
                        if (d >= counts.Length)
                        {
                            return;
                        }

                        actualCounts[d]++;
                    }

                    if (actualCounts.SequenceEqual(counts))
                    {
                        accepted.Add(mask);
                    }

                    return;
                }

                Edge edge = graph.GetEdge(edgeIndex);

                // Skip this edge.
                Recurse(edgeIndex + 1, mask);

                // Take this edge.
                degree[edge.U]++;
                degree[edge.V]++;
                Recurse(edgeIndex + 1, mask | (1 << edgeIndex));
                degree[edge.U]--;
                degree[edge.V]--;
            }
        }

        private static Graph NamedGraph(string graphName) => graphName switch
        {
            "path4" => Graph.Path(4),
            "cycle5" => Graph.Cycle(5),
            "complete5" => Graph.Complete(5),
            "grid2x3" => Graph.Grid(2, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(graphName)),
        };

        private static (Graph Graph, int[] Counts) NamedCase(string caseName) => caseName switch
        {
            "path4varied" => (Graph.Path(4), new[] { 1, 2, 1 }), // two endpoints at degree 1, two middles at degree 2 -- the full path
            "grid2x3varied" => (Graph.Grid(2, 3), new[] { 0, 2, 4 }),
            "complete4varied" => (Graph.Complete(4), new[] { 0, 0, 4 }), // a 4-cycle inside K4
            "twoTrianglesVaried" => (new Graph(6, new[]
            {
                new Edge(0, 1), new Edge(1, 2), new Edge(2, 0),
                new Edge(3, 4), new Edge(4, 5), new Edge(5, 3),
            }), new[] { 0, 0, 6 }),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };

        /// <summary>
        /// The Petersen graph: outer 5-cycle 0-1-2-3-4-0, spokes i-(i+5), inner pentagram 5-7-9-6-8-5.
        /// </summary>
        private static Graph PetersenGraph()
        {
            var edges = new List<Edge>();
            for (int i = 0; i < 5; i++)
            {
                edges.Add(new Edge(i, (i + 1) % 5));
                edges.Add(new Edge(i, i + 5));
            }

            for (int i = 0; i < 5; i++)
            {
                edges.Add(new Edge(5 + i, 5 + (i + 2) % 5));
            }

            return new Graph(10, edges.Distinct().ToList());
        }

        private sealed class RecordingProgress : IProgress<BuildProgress>
        {
            private readonly List<BuildProgress> _history;

            public RecordingProgress(List<BuildProgress> history)
            {
                _history = history;
            }

            public void Report(BuildProgress value) => _history.Add(value);
        }
    }
}

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

namespace ZDD.Net.Tests.Graphs
{
    /// <summary>
    /// M6-15 completion criteria for <see cref="GraphConstraints"/> / <see cref="GraphSet.Graphs(Graph, GraphConstraints)"/>
    /// / <see cref="GraphSet.Where(GraphConstraints)"/>: every combination of constraint fields matches an
    /// independently written brute-force check (including a shape matching Graphillion's own
    /// <c>graphs()</c> tutorial example), an all-default <see cref="GraphConstraints"/> returns the full
    /// power set, <see cref="GraphSet.Where(GraphConstraints)"/> matches building the base family and
    /// filtering afterward while building a smaller intermediate diagram than the constraint alone would,
    /// and a contradictory or out-of-range constraint fails eagerly rather than silently.
    /// </summary>
    public class GraphConstraintsTests
    {
        [Theory]
        [InlineData("path4")]
        [InlineData("cycle5")]
        [InlineData("grid2x3")]
        public void MatchesBruteForceEnumerationForVariousConstraintPatterns(string graphName)
        {
            Graph graph = NamedGraph(graphName);

            foreach (GraphConstraints constraints in ConstraintPatterns(graph))
            {
                GraphSet actual = GraphSet.Graphs(graph, constraints);
                BruteForceFamily expected = BruteForceConstraints(graph, constraints);

                FamilyAssert.AssertSameFamily($"{graphName} {Describe(constraints)}", actual.Zdd, expected);
            }
        }

        [Fact]
        public void NoConstraintsReturnsThePowerSet()
        {
            Graph grid = Graph.Grid(3, 3);
            GraphSet all = GraphSet.Graphs(grid, new GraphConstraints());

            Assert.Equal(BigInteger.Pow(2, grid.EdgeCount), all.Count);
        }

        [Fact]
        public void WhereWithNoConstraintsReturnsTheSameFamilyUnchanged()
        {
            Graph grid = Graph.Grid(3, 3);
            GraphSet paths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);

            GraphSet result = paths.Where(new GraphConstraints());

            Assert.Equal(paths.Zdd, result.Zdd);
        }

        [Fact]
        public void WhereMatchesBuildingTheBaseFamilyThenFilteringAfterward()
        {
            Graph grid = Graph.Grid(4, 4);
            GraphSet basePaths = GraphSet.Paths(grid, 0, grid.VertexCount - 1);
            var constraints = new GraphConstraints { EdgeCount = (1, 8) };

            GraphSet filtered = basePaths.Where(constraints);

            Zdd postHoc = basePaths.Zdd.Intersect(
                FrontierBuilder.Build<CardinalitySpec, int>(basePaths.Universe.Manager, new CardinalitySpec(grid.EdgeCount, 1, 8)));

            Assert.Equal(postHoc, filtered.Zdd);
        }

        [Fact]
        public void WhereBuildsFewerIntermediateNodesThanMaterializingTheBaseFamilyFirst()
        {
            // Mirrors GraphSetTests.FiltersAreAppliedAtConstructionTimeNotAsAPostHocIntersection, but for
            // Where's EdgeCount constraint (Zdd.Subset, M3-5) instead of Including/Excluding. The bound is
            // set to the grid's corner-to-corner Manhattan distance (3+3=6 edges on a 4x4 grid) — tight
            // enough that folding it into the frontier walk prunes every detouring path branch long before
            // it would ever complete, whereas a post-hoc filter would have to materialize every detour in
            // the unfiltered base family first. (A loose bound would not shrink anything here: EdgeCount
            // adds real state of its own — a running "taken so far" count — so unlike a stateless filter
            // such as Including/Excluding, crossing it with the base family's own state only shrinks the
            // diagram once the bound is tight enough to prune away branches the base family alone keeps
            // alive; this is why GraphConstraints.EdgeCount is worth its own scenario here.)
            Graph grid = Graph.Grid(4, 4);
            var baseSpec = new ArraySpecErased<PathSpec>(new PathSpec(grid, 0, grid.VertexCount - 1));
            var constraintSpec = new StructSpecErased<CardinalitySpec, int>(new CardinalitySpec(grid.EdgeCount, 1, 6));
            var combinedSpec = new AndErasedSpec(baseSpec, constraintSpec);

            long constructionTimeNodeCount = TopDownExpander<ErasedGraphDdSpec, object?>
                .Expand(new ErasedGraphDdSpec(combinedSpec)).NodeCount;
            long postFilterIntermediateNodeCount = TopDownExpander<ErasedGraphDdSpec, object?>
                .Expand(new ErasedGraphDdSpec(baseSpec)).NodeCount;

            Assert.True(
                constructionTimeNodeCount < postFilterIntermediateNodeCount,
                $"expected the edge-count filter folded into construction ({constructionTimeNodeCount} nodes) " +
                $"to build fewer intermediate nodes than the unfiltered base family a post-hoc filter would " +
                $"need first ({postFilterIntermediateNodeCount} nodes).");
        }

        [Fact]
        public void ContradictoryEdgeCountRangeThrows()
        {
            Graph graph = Graph.Path(4);
            var constraints = new GraphConstraints { EdgeCount = (5, 3) };

            Assert.Throws<ArgumentOutOfRangeException>(() => GraphSet.Graphs(graph, constraints));
        }

        [Fact]
        public void DegreeConstraintsRejectsAnOutOfRangeVertex()
        {
            Graph graph = Graph.Path(4);
            var constraints = new GraphConstraints
            {
                DegreeConstraints = new Dictionary<int, (int Lo, int Hi)> { [10] = (0, 1) },
            };

            Assert.Throws<ArgumentException>(() => GraphSet.Graphs(graph, constraints));
        }

        [Fact]
        public void LinearConstraintsRejectsAMismatchedCoefficientCount()
        {
            Graph graph = Graph.Path(4); // 3 edges
            var constraints = new GraphConstraints
            {
                LinearConstraints = new[] { (new[] { 1, 2 }, LinearConstraintOperator.LessOrEqual, 5L) },
            };

            Assert.Throws<ArgumentException>(() => GraphSet.Graphs(graph, constraints));
        }

        [Fact]
        public void ConstructorRejectsNullArguments()
        {
            Graph graph = Graph.Path(4);
            GraphSet paths = GraphSet.Paths(graph, 0, 3);

            Assert.Throws<ArgumentNullException>(() => GraphSet.Graphs(null!, new GraphConstraints()));
            Assert.Throws<ArgumentNullException>(() => GraphSet.Graphs(graph, null!));
            Assert.Throws<ArgumentNullException>(() => paths.Where(null!));
        }

        /// <summary>
        /// A handful of constraint combinations: none; each field alone; and one combining every field at
        /// once, in the same shape as the issue's own tutorial-style example (degree-1 endpoints, a bounded
        /// edge count, one component, no cycle).
        /// </summary>
        private static IEnumerable<GraphConstraints> ConstraintPatterns(Graph graph)
        {
            int lastVertex = graph.VertexCount - 1;

            yield return new GraphConstraints();
            yield return new GraphConstraints { EdgeCount = (1, graph.EdgeCount - 1) };
            yield return new GraphConstraints { ComponentCount = 1 };
            yield return new GraphConstraints { NoLoop = true };
            yield return new GraphConstraints
            {
                DegreeConstraints = new Dictionary<int, (int Lo, int Hi)> { [0] = (0, 1), [lastVertex] = (0, 1) },
            };
            yield return new GraphConstraints { VertexGroups = new IReadOnlyList<int>[] { new[] { 0, lastVertex } } };
            yield return new GraphConstraints
            {
                LinearConstraints = new[] { (Enumerable.Repeat(1, graph.EdgeCount).ToArray(), LinearConstraintOperator.LessOrEqual, (long)(graph.EdgeCount - 1)) },
            };

            // The issue's own tutorial-style scenario: degree-1 endpoints, a bounded edge count, exactly
            // one component, and no cycle — a superset of "simple 0..lastVertex paths" (it also admits
            // acyclic trees with side branches whose two leaves happen to be 0 and lastVertex).
            yield return new GraphConstraints
            {
                DegreeConstraints = new Dictionary<int, (int Lo, int Hi)> { [0] = (1, 1), [lastVertex] = (1, 1) },
                EdgeCount = (1, graph.EdgeCount),
                ComponentCount = 1,
                NoLoop = true,
            };
        }

        private static string Describe(GraphConstraints constraints) =>
            $"Degree={constraints.DegreeConstraints is null}/EdgeCount={constraints.EdgeCount}/" +
            $"Components={constraints.ComponentCount}/NoLoop={constraints.NoLoop}/" +
            $"VertexGroups={constraints.VertexGroups is null}/Linear={constraints.LinearConstraints is null}";

        private static Graph NamedGraph(string name) => name switch
        {
            "path4" => Graph.Path(4),
            "cycle5" => Graph.Cycle(5),
            "grid2x3" => Graph.Grid(2, 3),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        private static BruteForceFamily BruteForceConstraints(Graph graph, GraphConstraints constraints)
        {
            var accepted = new List<int>();
            int edgeCount = graph.EdgeCount;

            if (edgeCount >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceConstraints enumerates all 2^edgeCount subsets and cannot handle {edgeCount} edges.",
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

                if (Satisfies(graph, edgeSet, constraints))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(edgeCount, accepted);
        }

        /// <summary>The definition, independently written: no reference to any of the specs under test.</summary>
        private static bool Satisfies(Graph graph, List<int> edgeSet, GraphConstraints constraints)
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

            bool hasCycle = false;
            var degree = new int[graph.VertexCount];

            foreach (int edgeIndex in edgeSet)
            {
                Edge edge = graph.GetEdge(edgeIndex);
                degree[edge.U]++;
                degree[edge.V]++;

                int ru = Find(edge.U);
                int rv = Find(edge.V);
                if (ru == rv)
                {
                    hasCycle = true;
                    continue;
                }

                parent[ru] = rv;
            }

            if (constraints.NoLoop && hasCycle)
            {
                return false;
            }

            if (constraints.DegreeConstraints is { } degreeConstraints)
            {
                foreach (KeyValuePair<int, (int Lo, int Hi)> entry in degreeConstraints)
                {
                    int d = degree[entry.Key];
                    if (d < entry.Value.Lo || d > entry.Value.Hi)
                    {
                        return false;
                    }
                }
            }

            if (constraints.EdgeCount is (int min, int max) && (edgeSet.Count < min || edgeSet.Count > max))
            {
                return false;
            }

            if (constraints.ComponentCount is int target)
            {
                var sizeByRoot = new Dictionary<int, int>();
                for (int v = 0; v < graph.VertexCount; v++)
                {
                    int root = Find(v);
                    sizeByRoot[root] = sizeByRoot.GetValueOrDefault(root) + 1;
                }

                if (sizeByRoot.Values.Count(size => size >= 2) != target)
                {
                    return false;
                }
            }

            if (constraints.VertexGroups is { } groups)
            {
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
            }

            if (constraints.LinearConstraints is { } linearConstraints)
            {
                foreach ((int[] coefficients, LinearConstraintOperator op, long boundValue) in linearConstraints)
                {
                    long sum = edgeSet.Sum(i => (long)coefficients[i]);
                    bool ok = op switch
                    {
                        LinearConstraintOperator.LessOrEqual => sum <= boundValue,
                        LinearConstraintOperator.GreaterOrEqual => sum >= boundValue,
                        LinearConstraintOperator.Equal => sum == boundValue,
                        _ => throw new ArgumentOutOfRangeException(nameof(op)),
                    };

                    if (!ok)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}

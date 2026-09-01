using System;
using System.Collections.Generic;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// The 10 representative builds docs/benchmarks.md records baselines for (issue #31 / M2-11), shared
    /// between the timed <see cref="FrontierBuildBenchmarks"/> and the untimed <see cref="StatsReport"/>
    /// so the two never drift apart.
    /// </summary>
    /// <remarks>
    /// Each case takes the <see cref="BuildOptions"/> its *primary* <see cref="FrontierBuilder"/> call runs
    /// under, so <see cref="StatsReport"/> can pass a progress-recording one and get the frontier-width
    /// history docs/benchmarks.md's "peak frontier width" column comes from; the timed benchmarks pass null.
    /// For the Union/Product cases, "primary" is the first, larger operand — the one whose own top-down
    /// pass actually explores the frontier the case is representative of.
    /// </remarks>
    internal static class Cases
    {
        public static readonly IReadOnlyList<(string Name, Func<ZddManager, BuildOptions?, Zdd> Build, int VariableCount)> All =
            new (string, Func<ZddManager, BuildOptions?, Zdd>, int)[]
            {
                ("Path_Grid5x5", PathGrid(5), Graph.Grid(5, 5).EdgeCount),
                ("Path_Grid6x6", PathGrid(6), Graph.Grid(6, 6).EdgeCount),
                ("Path_Grid7x7", PathGrid(7), Graph.Grid(7, 7).EdgeCount),
                ("SpanningTree_Complete8", SpanningTreeComplete(8), Graph.Complete(8).EdgeCount),
                ("PerfectMatching_Grid6x6", PerfectMatchingGrid(6), Graph.Grid(6, 6).EdgeCount),
                ("Cardinality_5000Choose2400To2600", Cardinality(5000, 2400, 2600), 5000),
                ("LinearConstraint_1000ItemsKnapsack", LinearConstraint(1000), 1000),
                ("Forest_Grid5x5_TwoComponents", ForestGrid(5, 2), Graph.Grid(5, 5).EdgeCount),
                ("Union_TwoGrid6x6Paths", UnionOfTwoPaths(6), Graph.Grid(6, 6).EdgeCount),
                ("Product_Grid5x5PathsAndCardinality", ProductOfPathAndCardinality(5), Graph.Grid(5, 5).EdgeCount),
            };

        private static Func<ZddManager, BuildOptions?, Zdd> PathGrid(int n) => (manager, options) =>
        {
            Graph grid = Graph.Grid(n, n);
            return FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, grid.VertexCount - 1), options);
        };

        private static Func<ZddManager, BuildOptions?, Zdd> SpanningTreeComplete(int n) => (manager, options) =>
            FrontierBuilder.Build<SpanningTreeSpec>(manager, new SpanningTreeSpec(Graph.Complete(n)), options);

        private static Func<ZddManager, BuildOptions?, Zdd> PerfectMatchingGrid(int n) => (manager, options) =>
            FrontierBuilder.Build<MatchingSpec>(manager, new MatchingSpec(Graph.Grid(n, n), perfect: true), options);

        private static Func<ZddManager, BuildOptions?, Zdd> Cardinality(int itemCount, int min, int max) => (manager, options) =>
            FrontierBuilder.Build<CardinalitySpec, int>(manager, new CardinalitySpec(itemCount, min, max), options);

        private static Func<ZddManager, BuildOptions?, Zdd> LinearConstraint(int itemCount) => (manager, options) =>
        {
            int[] coefficients = new int[itemCount];
            long bound = 0;
            for (int i = 0; i < itemCount; i++)
            {
                // Deterministic pseudo-random weights (no external RNG dependency, same run to run).
                coefficients[i] = 1 + (int)(i * 2654435761u % 50);
                bound += coefficients[i];
            }

            bound /= 2;

            return FrontierBuilder.Build<LinearConstraintSpec, long>(
                manager, new LinearConstraintSpec(coefficients, LinearConstraintOperator.LessOrEqual, bound), options);
        };

        private static Func<ZddManager, BuildOptions?, Zdd> ForestGrid(int n, int components) => (manager, options) =>
            FrontierBuilder.Build<ForestSpec>(manager, new ForestSpec(Graph.Grid(n, n), components), options);

        private static Func<ZddManager, BuildOptions?, Zdd> UnionOfTwoPaths(int n) => (manager, options) =>
        {
            Graph grid = Graph.Grid(n, n);
            Zdd anyEndpoints = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, 0, allowAnyEndpoints: true), options);
            Zdd cornerToCorner = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, grid.VertexCount - 1));
            return anyEndpoints.Union(cornerToCorner);
        };

        private static Func<ZddManager, BuildOptions?, Zdd> ProductOfPathAndCardinality(int n) => (manager, options) =>
        {
            Graph grid = Graph.Grid(n, n);
            Zdd paths = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, grid.VertexCount - 1), options);
            Zdd atLeastHalfTheEdges = FrontierBuilder.Build<CardinalitySpec, int>(
                manager, new CardinalitySpec(grid.EdgeCount, grid.EdgeCount / 2, grid.EdgeCount));
            return paths.Product(atLeastHalfTheEdges);
        };
    }
}

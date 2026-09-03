using System;
using System.Collections.Generic;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// The cases docs/benchmarks.md's M4-8 section (Graphillion / TdZdd comparison, issue #51) times and
    /// measures memory for, beyond the 10 cases <see cref="Cases"/> already covers: the PLAN §10 target
    /// grid sizes <see cref="MemoryReport"/> and <see cref="BuildTimeReport"/> did not previously reach
    /// (8&#215;8, 9&#215;9, 11&#215;11 — 7&#215;7 is already the <c>"Path_Grid7x7"</c> entry in
    /// <see cref="Cases.All"/>), and an independent-set case (no existing case uses
    /// <see cref="IndependentSetSpec"/>, the one built-in spec whose variables are vertices rather than
    /// edges — the family algebra and spanning-tree/matching comparisons reuse
    /// <c>Cardinality_5000Choose2400To2600</c>, <c>SpanningTree_Complete8</c>, and
    /// <c>PerfectMatching_Grid6x6</c> from <see cref="Cases.All"/> directly).
    /// </summary>
    /// <remarks>
    /// These cases are folded into <see cref="MemoryReport.AllCases"/>, so the existing <c>-- time
    /// &lt;name&gt;</c> and <c>-- memory &lt;name&gt;</c> modes already measure them — no new report code
    /// or CLI mode is needed. <c>Path_Grid11x11</c> additionally needs an OS-level peak-RSS reading (the
    /// PLAN §10 goal is an 8 GB process budget, which the forced-GC managed-heap peak
    /// <see cref="MemoryReport"/> reports does not fully capture — native allocator overhead and
    /// fragmentation are outside it); that reading is taken externally with
    /// <c>/usr/bin/time -v dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- time
    /// Path_Grid11x11</c>, documented alongside the managed-heap figure in docs/benchmarks.md's M4-8
    /// section rather than reimplemented here.
    /// </remarks>
    internal static class ComparisonReport
    {
        public static IEnumerable<(string Name, Func<ZddManager, BuildOptions?, Zdd> Build, int VariableCount)> Cases()
        {
            yield return ("Path_Grid8x8", PathGrid(8), Graph.Grid(8, 8).EdgeCount);
            yield return ("Path_Grid9x9", PathGrid(9), Graph.Grid(9, 9).EdgeCount);
            yield return ("Path_Grid11x11", PathGrid(11), Graph.Grid(11, 11).EdgeCount);
            yield return ("IndependentSet_Grid6x6", IndependentSetGrid(6), Graph.Grid(6, 6).VertexCount);
        }

        private static Func<ZddManager, BuildOptions?, Zdd> PathGrid(int n) => (manager, options) =>
        {
            Graph grid = Graph.Grid(n, n);
            return FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, grid.VertexCount - 1), options);
        };

        private static Func<ZddManager, BuildOptions?, Zdd> IndependentSetGrid(int n) => (manager, options) =>
            FrontierBuilder.Build<IndependentSetSpec>(manager, new IndependentSetSpec(Graph.Grid(n, n)), options);
    }
}

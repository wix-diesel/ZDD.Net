# Graphillion / TdZdd comparison benchmarks

The code behind `docs/benchmarks.md`'s "M4-8: Graphillion / TdZdd との比較" section
(issue #51). It measures the same five cases three ways — this repository's own
`bench/ZDD.Net.Benchmarks` (see its README-equivalent doc comments and
`docs/benchmarks.md`), [Graphillion](http://graphillion.org/) (Python + C++ core), and
[TdZdd](https://github.com/kunisura/TdZdd) (C++, header-only) — so PLAN.md §10's
"Graphillion（C++コア）との比: 3倍以内、最終的に2倍以内" target has an actual number
behind it, not just ZDD.Net's own reading of itself.

This code is kept in git (not just run once and discarded) so the comparison can be
re-run later on different hardware — see each subdirectory's README for exact setup and
run steps:

- [`graphillion/`](graphillion/README.md) — Python scripts using Graphillion's
  `GraphSet` / `VertexSetSet` API.
- [`tdzdd/`](tdzdd/README.md) — C++ programs against TdZdd's headers (one of them,
  `independent_set.cpp`, is a small custom `DdSpec` we wrote — TdZdd ships no built-in
  independent-set spec, since its bundled specs are all edge-indexed).

## The five cases

Chosen to match issue #51's list (PLAN §10's target grid sizes, plus one representative
case each for spanning trees, matchings, independent sets, and Core-layer family
algebra) and, where ZDD.Net already had a case of the same shape, to reuse its exact
parameters so the three implementations are provably computing the same quantity:

| Case | Problem | Matches ZDD.Net case |
|---|---|---|
| `grid_paths` | s–t simple paths on an *n*×*n* grid, *n* ∈ {7, 8, 9, 11} | `Path_Grid7x7`/`8x8`/`9x9`/`11x11` |
| `spanning_tree` | Spanning trees of the complete graph *K*₈ | `SpanningTree_Complete8` |
| `matching` | Perfect matchings of a 6×6 grid | `PerfectMatching_Grid6x6` |
| `independent_set` | Independent vertex sets of a 6×6 grid | `IndependentSet_Grid6x6` |
| `cardinality` | Subsets of 5000 plain items with size in [2400, 2600] | `Cardinality_5000Choose2400To2600` |

Every case's count was cross-checked to match exactly across all three implementations
(and, for the grid path counts, against OEIS A007764) before any of this was measured
for time or memory — see docs/benchmarks.md's M4-8 section for the values.

## Reproducing the ZDD.Net side

```bash
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- time <CaseName>
dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- memory <CaseName>
```

`<CaseName>` is any name from the table above's right column, plus `Path_Grid7x7`
through `Path_Grid11x11` and `IndependentSet_Grid6x6`, which
`bench/ZDD.Net.Benchmarks/ComparisonReport.cs` adds to the existing case registry (see
its doc comment) — no separate CLI mode was needed. Run one case per process (as the
command above does) so an earlier case's pooled buffers do not make a later case look
artificially cheap, matching the convention `docs/benchmarks.md`'s M3-2 section already
established.

For `Path_Grid11x11`'s process-level peak RSS (used for the PLAN §10 "8 GB" goal, which
is a whole-process budget the managed-heap figure `-- memory` reports does not fully
capture — see docs/benchmarks.md's M4-8 section):

```bash
/usr/bin/time -v dotnet run -c Release --project bench/ZDD.Net.Benchmarks -- time Path_Grid11x11
```

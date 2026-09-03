# TdZdd comparison programs

C++ programs that compute the same five cases as `../graphillion/` and this
repository's own `bench/ZDD.Net.Benchmarks`, against
[TdZdd](https://github.com/kunisura/TdZdd) (MIT, header-only) — see `../README.md` for
the overview and `docs/benchmarks.md`'s M4-8 section for the results this produced
(issue #51). Four of the five reuse TdZdd's own bundled specs (`PathZdd`,
`FrontierBasedSearch`, `DegreeConstraint`, plus the "universal ZDD" constructor for the
cardinality case); `independent_set.cpp` is a small custom `DdSpec` written for this
comparison — TdZdd's bundled specs are all edge-indexed via `tdzdd::Graph`, and an
independent set needs vertex-indexed variables. Its doc comment explains the frontier
design (a sliding window over the last `cols` decided vertices).

## Setup

TdZdd is header-only — no build or install step of its own, just a checkout:

```bash
git clone https://github.com/kunisura/TdZdd.git
```

Verified against commit `95ad69d17cb375f4f87f282bf95e05b08cf53c09` (2025-08-03) with
g++ 13.3.0 (Ubuntu 13.3.0-6ubuntu2~24.04.1) — this repository's measurement environment
(see docs/benchmarks.md's "測定環境" section for the rest: same CPU/OS/RAM as the
ZDD.Net and Graphillion readings, so the three are directly comparable).

## Building

```bash
cd bench/comparison/tdzdd
make TDZDD_INCLUDE=/path/to/TdZdd/include
```

`TDZDD_INCLUDE` defaults to `../../../../TdZdd/include` (a `TdZdd` checkout as a sibling
of this repository's own checkout) — pass it explicitly if TdZdd lives elsewhere, as in
the example above.

## Running

```bash
./gen_grid 7 > grid7.dat && ./gen_grid 8 > grid8.dat && ./gen_grid 9 > grid9.dat
./gen_grid 11 > grid11.dat && ./gen_grid 6 > grid6.dat
./gen_complete 8 > complete8.dat

./grid_paths grid7.dat
./grid_paths grid8.dat
./grid_paths grid9.dat
./grid_paths grid11.dat
./spanning_tree complete8.dat
./matching grid6.dat
./independent_set 6
./cardinality 5000 2400 2600
```

`gen_grid`/`gen_complete` emit TdZdd's plain adjacency-list text format (the same one
`apps/ddpaths/G3x3.dat` in the TdZdd repository uses — `gen_grid 3` reproduces that file
exactly) so no external generator (Python or otherwise) is needed. Each benchmark prints
one line: elapsed wall time (`std::chrono::steady_clock`), peak RSS (`VmHWM` from
`/proc/self/status` — Linux-only, matching this repository's measurement environment),
final ZDD node count, and the exact count.

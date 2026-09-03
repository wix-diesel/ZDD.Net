# Graphillion comparison scripts

Python scripts that compute the same five cases as `../tdzdd/` and this repository's own
`bench/ZDD.Net.Benchmarks`, using [Graphillion](http://graphillion.org/)'s public
`GraphSet` / `VertexSetSet` API — see `../README.md` for the overview and
`docs/benchmarks.md`'s M4-8 section for the results this produced (issue #51).

## Setup

Graphillion ships as a source distribution on PyPI (it builds a C extension on install,
so a C++ toolchain is needed — `g++`/`cmake`, already required for `../tdzdd/`):

```bash
python3 -m venv .venv
source .venv/bin/activate
pip install graphillion
```

Verified against Graphillion 2.1 on Python 3.11.15 (this repository's measurement
environment — see docs/benchmarks.md's "測定環境" section for the rest: same CPU/OS/RAM
as the ZDD.Net and TdZdd readings, so the three are directly comparable). Install time
is a couple of minutes (it compiles Graphillion's bundled SAPPOROBDD-based C++ core).

## Running

```bash
for n in 7 8 9 11; do python3 grid_paths.py $n; done
python3 spanning_tree.py 8
python3 matching.py 6
python3 independent_set.py 6
python3 cardinality.py 5000 2400 2600
```

Each prints one line per case: elapsed wall time, peak RSS (`resource.getrusage(...).
ru_maxrss`, which is already KB on Linux), and the exact count. `grid_paths.py` accepts
several sizes in one invocation (`python3 grid_paths.py 7 8 9 11`), but the loop above
runs each size in its own process instead — Graphillion's universe and any already-built
`GraphSet`s stay live for the rest of the process otherwise, which biases memory readings
for whatever case runs after the first.

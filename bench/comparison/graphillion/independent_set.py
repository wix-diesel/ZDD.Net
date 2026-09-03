"""Independent-set counting on an n x n grid, via Graphillion's
VertexSetSet.independent_sets() — matches ZDD.Net's IndependentSetSpec
(IndependentSet_Grid6x6) and the custom TdZdd spec in independent_set.cpp. See
../README.md.

Usage: python3 independent_set.py <n>
"""
import sys

from common import Timer, report
from graphillion import VertexSetSet
from grid_paths import grid_edges


def run(n):
    edges = grid_edges(n)
    vertices = list(range(1, n * n + 1))
    VertexSetSet.set_universe(vertices)

    timer = Timer()
    vss = VertexSetSet.independent_sets(edges)
    count = vss.len()
    elapsed_ms = timer.elapsed_ms()

    report(f"IndependentSet_Grid{n}x{n}", elapsed_ms, count)


if __name__ == "__main__":
    for arg in sys.argv[1:]:
        run(int(arg))

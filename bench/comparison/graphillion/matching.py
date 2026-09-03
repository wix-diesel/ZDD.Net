"""Perfect matching counting via Graphillion's GraphSet.perfect_matchings(), on an
n x n grid — matches ZDD.Net's MatchingSpec(perfect: true) (PerfectMatching_Grid6x6) and
TdZdd's DegreeConstraint (matching.cpp). See ../README.md.

Usage: python3 matching.py <n>
"""
import sys

from common import Timer, report
from graphillion import GraphSet
from grid_paths import grid_edges


def run(n):
    edges = grid_edges(n)
    GraphSet.set_universe(edges)

    timer = Timer()
    gs = GraphSet.perfect_matchings()
    count = gs.len()
    elapsed_ms = timer.elapsed_ms()

    report(f"PerfectMatching_Grid{n}x{n}", elapsed_ms, count)


if __name__ == "__main__":
    for arg in sys.argv[1:]:
        run(int(arg))

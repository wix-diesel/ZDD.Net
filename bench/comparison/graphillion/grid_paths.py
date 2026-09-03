"""s-t simple path counting on an n x n grid, via Graphillion's GraphSet.paths() — the
PLAN.md Section 10 target case, compared against ZDD.Net's PathSpec (Path_GridNxN) and
TdZdd's PathZdd (grid_paths.cpp). See ../README.md for setup.

Usage: python3 grid_paths.py <n>
"""
import sys

from common import Timer, report
from graphillion import GraphSet


def grid_edges(n):
    """Row-major n x n grid, vertex (r, c) numbered r*n+c+1 — vertex 1 and vertex n*n
    are opposite corners, matching ZDD.Net's Graph.Grid(n, n) (PathSpec(grid, 0,
    grid.VertexCount - 1)) and TdZdd's gen_grid.cpp / setDefaultPathColor()."""
    edges = []

    def v(r, c):
        return r * n + c + 1

    for r in range(n):
        for c in range(n):
            if c + 1 < n:
                edges.append((v(r, c), v(r, c + 1)))
            if r + 1 < n:
                edges.append((v(r, c), v(r + 1, c)))
    return edges


def run(n):
    edges = grid_edges(n)
    GraphSet.set_universe(edges)

    timer = Timer()
    gs = GraphSet.paths(1, n * n)
    count = gs.len()
    elapsed_ms = timer.elapsed_ms()

    report(f"Path_Grid{n}x{n}", elapsed_ms, count)


if __name__ == "__main__":
    for arg in sys.argv[1:]:
        run(int(arg))

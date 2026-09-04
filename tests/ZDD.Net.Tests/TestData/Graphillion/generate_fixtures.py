"""Generates the Graphillion-format fixture files in this directory, used by
GraphillionTextFormatTests to check that ZDD.Net actually reads real Graphillion output (not
just its own writer's output) — see docs/ROADMAP.md's M5-2 row / issue #54.

Setup (same as bench/comparison/graphillion/README.md):

    python3 -m venv .venv
    source .venv/bin/activate
    pip install graphillion   # verified against graphillion==2.1 on Python 3.11

Usage, from this directory:

    python3 generate_fixtures.py

Regenerate only if Graphillion's dump format itself ever changes (it hasn't since this was
written) — the fixture files are checked into the repository precisely so nobody else needs
Graphillion installed just to run the ZDD.Net test suite.
"""
from graphillion import GraphSet


def write_dump(path, gs):
    with open(path, "w") as f:
        gs.dump(f)


def triangle_family():
    """A tiny, hand-verifiable, asymmetric family: not a graph problem at all, just three
    elements with a family shaped so that getting the elem<->item direction backwards would
    change *which* family gets read back (a symmetric family couldn't catch that bug).

    universe (traversal='as-is', so elem 1/2/3 are exactly edges 1/2/3 in this order):
      e1 = (1, 2), e2 = (1, 3), e3 = (2, 3)
    family: {{e1, e3}, {e2}}  i.e. {{(1,2), (2,3)}, {(1,3)}}
    """
    GraphSet.set_universe([(1, 2), (1, 3), (2, 3)], traversal="as-is")
    gs = GraphSet([[(1, 2), (2, 3)], [(1, 3)]])
    assert gs.len() == 2
    write_dump("triangle_family.zdd.txt", gs)


def grid_3x2_paths():
    """Simple s-t paths on a 3-row x 2-column grid graph (asymmetric: 7 edges, corner to
    corner), matching ZDD.Net's Graph.Grid(3, 2) edge order and vertex numbering exactly
    (vertex (r, c) -> r*cols+c, 0-based; edges: row r's horizontal edges first, then row r's
    vertical edges to row r+1, for r = 0..rows-1) so a 'traversal=as-is' universe lines up
    with GraphillionTextFormatTests's ZDD.Net-side reconstruction one item at a time.
    """
    rows, cols = 3, 2

    def v(r, c):
        return r * cols + c

    edges = []
    for r in range(rows):
        for c in range(cols - 1):
            edges.append((v(r, c), v(r, c + 1)))
        if r < rows - 1:
            for c in range(cols):
                edges.append((v(r, c), v(r + 1, c)))

    GraphSet.set_universe(edges, traversal="as-is")
    gs = GraphSet.paths(v(0, 0), v(rows - 1, cols - 1))
    print(f"grid_3x2_paths: {len(edges)} edges, count = {gs.len()}")
    write_dump("grid_3x2_paths.zdd.txt", gs)


if __name__ == "__main__":
    triangle_family()
    grid_3x2_paths()
    print("Wrote triangle_family.zdd.txt and grid_3x2_paths.zdd.txt")

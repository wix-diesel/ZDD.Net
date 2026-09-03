"""Core family-algebra representative case: subsets of n plain items whose size falls in
[min, max] (a cardinality-window constraint, no graph structure involved) — matches
ZDD.Net's CardinalitySpec (Cardinality_5000Choose2400To2600) and TdZdd's
powerset+SizeConstraint (cardinality.cpp).

Graphillion has no plain "n items" universe, only graphs, so this uses a dummy graph of
n disjoint edges (vertices (2i-1, 2i) for i=1..n) purely as a way to get n independent
boolean variables — GraphSet.graphs(num_edges=...) does not require connectivity, so it
returns exactly the edge subsets whose size is in the window, which is exactly the
cardinality-window family over n items. See ../README.md.

Usage: python3 cardinality.py <n> <min> <max>
"""
import sys

from common import Timer, report
from graphillion import GraphSet


def run(n, lo, hi):
    edges = [(2 * i - 1, 2 * i) for i in range(1, n + 1)]
    GraphSet.set_universe(edges)

    timer = Timer()
    gs = GraphSet.graphs(num_edges=range(lo, hi + 1))
    count = gs.len()
    elapsed_ms = timer.elapsed_ms()

    report(f"Cardinality_{n}Choose{lo}To{hi}", elapsed_ms, count)


if __name__ == "__main__":
    n, lo, hi = (int(a) for a in sys.argv[1:4])
    run(n, lo, hi)

"""Spanning tree counting via Graphillion's GraphSet.trees(), on the complete graph
K_n — matches ZDD.Net's SpanningTreeSpec (SpanningTree_Complete8) and TdZdd's
FrontierBasedSearch+SizeConstraint (spanning_tree.cpp). See ../README.md.

Usage: python3 spanning_tree.py <n>
"""
import sys

from common import Timer, report
from graphillion import GraphSet


def complete_edges(n):
    return [(u, v) for u in range(1, n + 1) for v in range(u + 1, n + 1)]


def run(n):
    edges = complete_edges(n)
    GraphSet.set_universe(edges)

    timer = Timer()
    gs = GraphSet.trees(is_spanning=True)
    count = gs.len()
    elapsed_ms = timer.elapsed_ms()

    report(f"SpanningTree_Complete{n}", elapsed_ms, count)


if __name__ == "__main__":
    for arg in sys.argv[1:]:
        run(int(arg))

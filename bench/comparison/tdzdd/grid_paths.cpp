// s-t simple path counting on an n x n grid, via TdZdd's PathZdd (SIMPATH) spec — the
// PLAN.md §10 target case, compared against ZDD.Net's PathSpec (Path_GridNxN) and
// Graphillion's GraphSet.paths() (grid_paths.py). See ../README.md for build/run steps.
//
// Usage: ./grid_paths gridN.dat
#include <tdzdd/DdStructure.hpp>
#include <tdzdd/spec/PathZdd.hpp>
#include <tdzdd/util/Graph.hpp>

#include "common.hpp"

using namespace tdzdd;

int main(int argc, char** argv) {
    if (argc != 2) {
        std::fprintf(stderr, "usage: %s <grid.dat>\n", argv[0]);
        return 1;
    }

    Graph graph;
    graph.readAdjacencyList(argv[1]);
    graph.setDefaultPathColor(); // colors vertex 1 and the highest-numbered vertex: the two opposite corners.

    bench::Timer timer;
    PathZdd spec(graph, /*lookahead=*/true);
    DdStructure<2> f(spec);
    f.zddReduce();
    double elapsedMs = timer.elapsedMs();

    bench::report(argv[1], elapsedMs, f.evaluate(ZddCardinality<>()), f.size());
    return 0;
}

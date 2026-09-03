// Perfect matching counting via TdZdd's DegreeConstraint: every vertex constrained to
// degree exactly 1. Compared against ZDD.Net's MatchingSpec(perfect: true)
// (PerfectMatching_Grid6x6) and Graphillion's GraphSet.perfect_matchings()
// (matching.py). See ../README.md.
//
// Usage: ./matching gridN.dat
#include <tdzdd/DdStructure.hpp>
#include <tdzdd/spec/DegreeConstraint.hpp>
#include <tdzdd/util/Graph.hpp>
#include <tdzdd/util/IntSubset.hpp>

#include "common.hpp"

using namespace tdzdd;

int main(int argc, char** argv) {
    if (argc != 2) {
        std::fprintf(stderr, "usage: %s <grid.dat>\n", argv[0]);
        return 1;
    }

    Graph graph;
    graph.readAdjacencyList(argv[1]);

    bench::Timer timer;
    IntRange exactlyOne(1, 1);
    DegreeConstraint dc(graph, &exactlyOne);
    DdStructure<2> f(dc);
    f.zddReduce();
    double elapsedMs = timer.elapsedMs();

    bench::report(argv[1], elapsedMs, f.evaluate(ZddCardinality<>()), f.size());
    return 0;
}

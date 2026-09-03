// Spanning tree counting via TdZdd's FrontierBasedSearch (forest mode: noLoop=true),
// restricted to exactly one component (uec=1) and exactly V-1 edges. A forest on V
// vertices with c components (isolated vertices counting as their own component) has
// V-c edges; forcing edges == V-1 forces c == 1, i.e. every vertex lies in that single
// component — a spanning tree. Compared against ZDD.Net's SpanningTreeSpec
// (SpanningTree_Complete8) and Graphillion's GraphSet.trees() (spanning_tree.py). See
// ../README.md.
//
// Usage: ./spanning_tree completeN.dat
#include <tdzdd/DdStructure.hpp>
#include <tdzdd/spec/FrontierBasedSearch.hpp>
#include <tdzdd/spec/SizeConstraint.hpp>
#include <tdzdd/util/Graph.hpp>
#include <tdzdd/util/IntSubset.hpp>

#include "common.hpp"

using namespace tdzdd;

int main(int argc, char** argv) {
    if (argc != 2) {
        std::fprintf(stderr, "usage: %s <complete.dat>\n", argv[0]);
        return 1;
    }

    Graph graph;
    graph.readAdjacencyList(argv[1]);
    int const v = graph.vertexSize();

    bench::Timer timer;
    FrontierBasedSearch fbs(graph, /*numUEC=*/1, /*noLoop=*/true);
    DdStructure<2> f(fbs);
    f.zddReduce();

    IntRange exactlyVMinus1(v - 1, v - 1);
    SizeConstraint sc(graph.edgeSize(), &exactlyVMinus1);
    f.zddSubset(sc);
    f.zddReduce();
    double elapsedMs = timer.elapsedMs();

    bench::report(argv[1], elapsedMs, f.evaluate(ZddCardinality<>()), f.size());
    return 0;
}

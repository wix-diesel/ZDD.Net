// Core family-algebra representative case: subsets of n plain items whose size falls in
// [min, max] (a cardinality-window constraint, no graph involved). Built as the full
// powerset ZDD (TdZdd's "universal ZDD" constructor) filtered by SizeConstraint —
// compared against ZDD.Net's CardinalitySpec (Cardinality_5000Choose2400To2600) and
// Graphillion's GraphSet.graphs(num_edges=...) over a dummy n-edge universe
// (cardinality.py, which has no vertex structure to speak of either — it only uses
// num_edges). See ../README.md.
//
// Usage: ./cardinality <n> <min> <max>
#include <cstdlib>

#include <tdzdd/DdStructure.hpp>
#include <tdzdd/spec/SizeConstraint.hpp>
#include <tdzdd/util/IntSubset.hpp>

#include "common.hpp"

using namespace tdzdd;

int main(int argc, char** argv) {
    if (argc != 4) {
        std::fprintf(stderr, "usage: %s <n> <min> <max>\n", argv[0]);
        return 1;
    }

    int n = std::atoi(argv[1]);
    int lo = std::atoi(argv[2]);
    int hi = std::atoi(argv[3]);

    bench::Timer timer;
    DdStructure<2> f(n); // full powerset of n items
    IntRange window(lo, hi);
    SizeConstraint sc(n, &window);
    f.zddSubset(sc);
    f.zddReduce();
    double elapsedMs = timer.elapsedMs();

    char label[64];
    std::snprintf(label, sizeof(label), "Cardinality_%dChoose%dTo%d", n, lo, hi);
    bench::report(label, elapsedMs, f.evaluate(ZddCardinality<>()), f.size());
    return 0;
}

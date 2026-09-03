// Independent-set counting on an n x n grid graph, via a small custom TdZdd DdSpec
// written for this comparison (TdZdd ships no independent-set spec; its bundled specs
// are all edge-indexed via tdzdd::Graph, but an independent set needs vertex-indexed
// variables). Compared against ZDD.Net's IndependentSetSpec (IndependentSet_Grid6x6) and
// Graphillion's VertexSetSet.independent_sets() (independent_set.py). See ../README.md.
//
// Design (a "broken profile" frontier over vertices, processed row-major):
//   Variables are grid vertices, decided in row-major order 0..rows*cols-1 (vertex
//   (r, c) is index r*cols + c), branch 1 meaning "included in the set". The state is a
//   sliding window of `cols` bits: right before vertex `idx` is decided, window[j] holds
//   the already-decided value of vertex (idx - cols + j), for j = 0..cols-1. So
//   window[0] is idx's top neighbor (idx - cols) and window[cols-1] is idx's left
//   neighbor (idx - 1, only a real neighbor when idx is not in column 0). After idx is
//   decided, the window slides: the oldest entry (idx - cols, whose only still-relevant
//   neighbor — idx itself — has now been checked) drops off the front, and idx's own
//   value is appended at the back. Every grid edge is checked exactly once this way: a
//   horizontal edge when its right endpoint is decided (as a left-neighbor check), a
//   vertical edge when its bottom endpoint is decided (as a top-neighbor check).
//
// Usage: ./independent_set <n>
#include <cstdlib>
#include <vector>

#include <tdzdd/DdSpec.hpp>
#include <tdzdd/DdStructure.hpp>

#include "common.hpp"

using namespace tdzdd;

class IndependentSetZdd: public PodArrayDdSpec<IndependentSetZdd,char,2> {
    int const cols;
    int const n;

public:
    IndependentSetZdd(int rows, int cols) : cols(cols), n(rows * cols) {
        setArraySize(cols);
    }

    int getRoot(char* state) const {
        for (int i = 0; i < cols; ++i) {
            state[i] = 0; // a virtual all-excluded row above row 0: never blocks inclusion.
        }
        return n;
    }

    int getChild(char* state, int level, int value) const {
        int idx = n - level;
        int c = idx % cols;

        if (value) {
            if (c > 0 && state[cols - 1]) return 0;      // left neighbor already included
            if (idx >= cols && state[0]) return 0;        // top neighbor already included
        }

        for (int i = 0; i + 1 < cols; ++i) {
            state[i] = state[i + 1];
        }
        state[cols - 1] = static_cast<char>(value);

        int remaining = level - 1;
        return remaining > 0 ? remaining : -1;
    }
};

int main(int argc, char** argv) {
    if (argc != 2) {
        std::fprintf(stderr, "usage: %s <n>\n", argv[0]);
        return 1;
    }

    int n = std::atoi(argv[1]);
    if (n <= 0) {
        std::fprintf(stderr, "n must be positive\n");
        return 1;
    }

    bench::Timer timer;
    IndependentSetZdd spec(n, n);
    DdStructure<2> f(spec);
    f.zddReduce();
    double elapsedMs = timer.elapsedMs();

    char label[64];
    std::snprintf(label, sizeof(label), "IndependentSet_Grid%dx%d", n, n);
    bench::report(label, elapsedMs, f.evaluate(ZddCardinality<>()), f.size());
    return 0;
}

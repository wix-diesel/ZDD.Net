// Generates the complete graph K_n in TdZdd's plain adjacency-list format, matching
// ZDD.Net's Graph.Complete(n) and Graphillion's GraphSet.trees() input for
// spanning_tree.py — used by spanning_tree.cpp (issue #51 / M4-8).
//
// Usage: ./gen_complete <n> > completeN.dat
#include <cstdio>
#include <cstdlib>

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

    for (int v = 1; v <= n; ++v) {
        bool first = true;
        for (int u = 1; u <= n; ++u) {
            if (u == v) continue;
            if (!first) std::fputc(' ', stdout);
            std::printf("%d", u);
            first = false;
        }
        std::fputc('\n', stdout);
    }

    return 0;
}

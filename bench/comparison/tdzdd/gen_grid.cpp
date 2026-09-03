// Generates an n x n grid graph in TdZdd's plain adjacency-list format (one line per
// vertex, listing its neighbors — see apps/ddpaths/G3x3.dat in the TdZdd repository) and
// prints it to stdout. Vertices are numbered 1..n*n in row-major order, so vertex 1 is
// grid corner (0,0) and vertex n*n is the opposite corner (n-1,n-1); TdZdd's
// Graph::setDefaultPathColor() picks exactly those two as the s-t pair, matching
// ZDD.Net's Graph.Grid(n, n) convention (PathSpec(grid, 0, grid.VertexCount - 1)) and
// Graphillion's GraphSet.paths(1, n*n) in grid_paths.py.
//
// No TdZdd headers are needed for this file — it only emits the text format TdZdd's own
// Graph::readAdjacencyList() reads.
//
// Usage: ./gen_grid <n> > gridN.dat
#include <cstdio>
#include <cstdlib>
#include <set>
#include <vector>

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

    auto vertex = [n](int r, int c) { return r * n + c + 1; };
    std::vector<std::set<int>> adjacency(n * n + 1);

    for (int r = 0; r < n; ++r) {
        for (int c = 0; c < n; ++c) {
            int a = vertex(r, c);
            if (c + 1 < n) {
                int b = vertex(r, c + 1);
                adjacency[a].insert(b);
                adjacency[b].insert(a);
            }
            if (r + 1 < n) {
                int b = vertex(r + 1, c);
                adjacency[a].insert(b);
                adjacency[b].insert(a);
            }
        }
    }

    for (int v = 1; v <= n * n; ++v) {
        bool first = true;
        for (int neighbor : adjacency[v]) {
            if (!first) std::fputc(' ', stdout);
            std::printf("%d", neighbor);
            first = false;
        }
        std::fputc('\n', stdout);
    }

    return 0;
}

using System.Numerics;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// Counts a graph's spanning trees via Kirchhoff's matrix-tree theorem, entirely independently of the
    /// ZDD machinery: build the Laplacian, delete any one row/column, and take the determinant of what's
    /// left. Used by <c>SpanningTreeSpecTests</c> as an external cross-check (docs/PLAN.md §11-4) that
    /// owes nothing to the frontier-method code it is verifying.
    /// </summary>
    internal static class Kirchhoff
    {
        /// <summary>The number of spanning trees of <paramref name="graph"/>.</summary>
        public static BigInteger CountSpanningTrees(Graph graph)
        {
            int n = graph.VertexCount;
            if (n == 1)
            {
                return BigInteger.One; // the empty edge set, trivially
            }

            var laplacian = new BigInteger[n, n];
            foreach (Edge edge in graph.Edges)
            {
                laplacian[edge.U, edge.U] += 1;
                laplacian[edge.V, edge.V] += 1;
                laplacian[edge.U, edge.V] -= 1;
                laplacian[edge.V, edge.U] -= 1;
            }

            // Any cofactor of the Laplacian gives the spanning tree count; deleting row/column 0 is as
            // good as any other choice.
            var minor = new BigInteger[n - 1, n - 1];
            for (int i = 1; i < n; i++)
            {
                for (int j = 1; j < n; j++)
                {
                    minor[i - 1, j - 1] = laplacian[i, j];
                }
            }

            return BigInteger.Abs(Determinant(minor));
        }

        /// <summary>
        /// The determinant of a square integer matrix, computed exactly via the Bareiss algorithm
        /// (fraction-free Gaussian elimination: every division performed is guaranteed to be exact, so no
        /// rational arithmetic is needed even though intermediate entries are not the matrix's own minors).
        /// </summary>
        private static BigInteger Determinant(BigInteger[,] matrix)
        {
            int n = matrix.GetLength(0);
            if (n == 0)
            {
                return BigInteger.One;
            }

            BigInteger[,] m = (BigInteger[,])matrix.Clone();
            BigInteger previousPivot = BigInteger.One;
            int sign = 1;

            for (int k = 0; k < n - 1; k++)
            {
                if (m[k, k] == BigInteger.Zero)
                {
                    int pivotRow = -1;
                    for (int i = k + 1; i < n; i++)
                    {
                        if (m[i, k] != BigInteger.Zero)
                        {
                            pivotRow = i;
                            break;
                        }
                    }

                    if (pivotRow < 0)
                    {
                        return BigInteger.Zero; // the whole remaining column is zero: singular
                    }

                    for (int j = 0; j < n; j++)
                    {
                        (m[k, j], m[pivotRow, j]) = (m[pivotRow, j], m[k, j]);
                    }

                    sign = -sign;
                }

                for (int i = k + 1; i < n; i++)
                {
                    for (int j = k + 1; j < n; j++)
                    {
                        m[i, j] = (m[i, j] * m[k, k] - m[i, k] * m[k, j]) / previousPivot;
                    }
                }

                previousPivot = m[k, k];
            }

            return sign * m[n - 1, n - 1];
        }
    }
}

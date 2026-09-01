using System.Numerics;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// Counts a graph's matchings via bitmask DP over subsets of vertices, entirely independently of the
    /// ZDD machinery. Used by <c>MatchingSpecTests</c> as an external cross-check (docs/PLAN.md §11-4:
    /// "完全マッチング数 = パーマネント／bitmask DP で照合") that owes nothing to the frontier-method code
    /// it is verifying.
    /// </summary>
    internal static class BitmaskMatchingCounter
    {
        /// <summary>The number of matchings of <paramref name="graph"/>, of any size (the empty matching counts).</summary>
        public static BigInteger CountMatchings(Graph graph)
        {
            int n = graph.VertexCount;
            int[][] neighbors = Neighbors(graph);
            int fullMask = n == 0 ? 0 : (1 << n) - 1;
            var memo = new BigInteger?[fullMask + 1];

            return CountFrom(0);

            BigInteger CountFrom(int used)
            {
                if (used == fullMask)
                {
                    return BigInteger.One;
                }

                if (memo[used] is BigInteger cached)
                {
                    return cached;
                }

                int v = LowestUnsetBit(used, n);
                BigInteger total = CountFrom(used | (1 << v)); // leave v unmatched

                foreach (int u in neighbors[v])
                {
                    if ((used & (1 << u)) == 0)
                    {
                        total += CountFrom(used | (1 << v) | (1 << u)); // match v-u
                    }
                }

                memo[used] = total;
                return total;
            }
        }

        /// <summary>The number of perfect matchings of <paramref name="graph"/> (0 if <c>VertexCount</c> is odd).</summary>
        public static BigInteger CountPerfectMatchings(Graph graph)
        {
            int n = graph.VertexCount;
            if ((n & 1) != 0)
            {
                return BigInteger.Zero;
            }

            int[][] neighbors = Neighbors(graph);
            int fullMask = n == 0 ? 0 : (1 << n) - 1;
            var memo = new BigInteger?[fullMask + 1];

            return CountFrom(0);

            BigInteger CountFrom(int used)
            {
                if (used == fullMask)
                {
                    return BigInteger.One;
                }

                if (memo[used] is BigInteger cached)
                {
                    return cached;
                }

                int v = LowestUnsetBit(used, n);
                BigInteger total = BigInteger.Zero;

                foreach (int u in neighbors[v])
                {
                    if ((used & (1 << u)) == 0)
                    {
                        total += CountFrom(used | (1 << v) | (1 << u)); // v must be matched
                    }
                }

                memo[used] = total;
                return total;
            }
        }

        private static int LowestUnsetBit(int used, int n)
        {
            for (int v = 0; v < n; v++)
            {
                if ((used & (1 << v)) == 0)
                {
                    return v;
                }
            }

            return n; // unreachable when used != fullMask
        }

        private static int[][] Neighbors(Graph graph)
        {
            int n = graph.VertexCount;
            var neighbors = new int[n][];

            for (int v = 0; v < n; v++)
            {
                System.Collections.Generic.IReadOnlyList<int> incident = graph.IncidentEdges(v);
                var list = new int[incident.Count];
                for (int i = 0; i < incident.Count; i++)
                {
                    Edge edge = graph.GetEdge(incident[i]);
                    list[i] = edge.U == v ? edge.V : edge.U;
                }

                neighbors[v] = list;
            }

            return neighbors;
        }
    }
}

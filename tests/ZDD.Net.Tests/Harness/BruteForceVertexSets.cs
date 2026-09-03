using System;
using System.Collections.Generic;
using ZDD.Net.Graphs;

namespace ZDD.Net.Tests.Harness
{
    /// <summary>
    /// Enumerates all <c>2^VertexCount</c> vertex subsets of a small graph and keeps the ones a caller-
    /// supplied predicate accepts, for cross-checking vertex-indexed specs (<c>IndependentSetSpec</c> and
    /// friends, M3-6) against a definition written independently of the frontier-method code being
    /// verified (docs/PLAN.md §11-1, the same role <see cref="BruteForceFamily"/> plays for edge-indexed
    /// specs).
    /// </summary>
    internal static class BruteForceVertexSets
    {
        /// <summary>
        /// Enumerates every vertex subset of <paramref name="graph"/> and returns the ones for which
        /// <paramref name="accepts"/> — given the graph and a membership array (<c>true</c> at index
        /// <c>v</c> means vertex <c>v</c> is in the subset) — returns <see langword="true"/>.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="graph"/> has 31 or more vertices.</exception>
        public static BruteForceFamily Enumerate(Graph graph, Func<Graph, bool[], bool> accepts)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(accepts);

            int n = graph.VertexCount;
            if (n >= 31)
            {
                throw new ArgumentException(
                    $"BruteForceVertexSets enumerates all 2^VertexCount subsets and cannot handle {n} vertices.",
                    nameof(graph));
            }

            var accepted = new List<int>();
            int bound = 1 << n;
            var membership = new bool[n];

            for (int mask = 0; mask < bound; mask++)
            {
                for (int v = 0; v < n; v++)
                {
                    membership[v] = (mask & (1 << v)) != 0;
                }

                if (accepts(graph, membership))
                {
                    accepted.Add(mask);
                }
            }

            return BruteForceFamily.FromMasks(n, accepted);
        }

        /// <summary>Turns one enumerated <see cref="Core.Zdd.Sets"/> vertex array into a membership array.</summary>
        public static bool[] ToMembership(Graph graph, int[] vertexSet)
        {
            var membership = new bool[graph.VertexCount];
            foreach (int v in vertexSet)
            {
                membership[v] = true;
            }

            return membership;
        }
    }
}

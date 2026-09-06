using System;
using ZDD.Net.Core;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// The two pieces of <see cref="GraphSet"/>'s and <see cref="DirectedGraphSet"/>'s factory methods
    /// (M8-1) that are worth sharing rather than duplicating: the full item list a power set is built
    /// over, and the validation <c>FromZdd</c> performs on a caller-supplied <see cref="Zdd"/>.
    /// </summary>
    internal static class GraphSetFactory
    {
        /// <summary>Returns <c>{0, 1, ..., count - 1}</c>, the item indices of every edge of a graph.</summary>
        public static int[] AllItems(int count)
        {
            var items = new int[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = i;
            }

            return items;
        }

        /// <summary>
        /// Checks that <paramref name="zdd"/> can be read as a family of edge sets over
        /// <paramref name="edgeCount"/> edges, and returns the level offset
        /// <see cref="PrecomputedZddSpec"/> needs to read it against a manager of exactly that size.
        /// </summary>
        /// <param name="zdd">The caller-supplied family.</param>
        /// <param name="edgeCount">The graph's edge count, i.e. the reading manager's variable count.</param>
        /// <param name="paramName">The public parameter name to blame in a thrown exception.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="zdd"/>'s manager has fewer variables than <paramref name="edgeCount"/>, or some
        /// member set uses an item index that is not an edge index (the message names it).
        /// </exception>
        public static int ValidateZddOver(Zdd zdd, int edgeCount, string paramName)
        {
            int variableCount = zdd.Manager.VariableCount; // Also validates the handle.

            if (variableCount < edgeCount)
            {
                throw new ArgumentException(
                    $"'{paramName}' is built over a manager with {variableCount} variable(s), but the graph has " +
                    $"{edgeCount} edge(s); a family cannot be read against a graph its manager has no variables for.",
                    paramName);
            }

            // Only a wider manager can hold an item that is not an edge index, so the support walk
            // (O(node count), and the common case is a family built over this very graph) is skipped
            // when the counts already agree.
            if (variableCount > edgeCount)
            {
                foreach (int item in zdd.Support())
                {
                    if (item >= edgeCount)
                    {
                        throw new ArgumentException(
                            $"'{paramName}' uses item index {item}, which is not an edge index of the graph " +
                            $"(0 .. {edgeCount - 1}).",
                            paramName);
                    }
                }
            }

            // level = VariableCount - item on both sides, so a larger source manager puts every item
            // this many levels higher than the manager the family is being read into.
            return variableCount - edgeCount;
        }
    }
}

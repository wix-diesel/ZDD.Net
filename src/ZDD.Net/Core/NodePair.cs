using System;
using System.Diagnostics;

namespace ZDD.Net.Core
{
    /// <summary>Decomposes a binary operation's subproblem (a pair of nodes) by one level.</summary>
    /// <remarks>
    /// Splitting at the higher (root-side) of the two nodes' top levels is common to every
    /// operation over a pair; only how the resulting four fragments get recombined differs, so
    /// that shared step lives here. <see cref="BinaryOperations"/> has its own inlined decompose
    /// (it builds subproblem keys at the same time); callers that want the raw fragments
    /// (<see cref="FamilyAlgebraOperations"/>, <see cref="ContainmentOperations"/>) use this.
    /// Values are read out and returned by value rather than by <c>ref</c>, since
    /// <see cref="UniqueTable.GetNode"/> can grow the table and invalidate old references.
    /// </remarks>
    internal static class NodePair
    {
        /// <summary>Splits subproblem <c>(f, g)</c> by one level, at the higher of the two nodes' top levels.</summary>
        /// <param name="nodes">The node table.</param>
        /// <param name="f">Left operand's node ID.</param>
        /// <param name="g">Right operand's node ID.</param>
        /// <param name="level">The level split on (1 or higher).</param>
        /// <param name="f0"><paramref name="f"/>'s side excluding the item.</param>
        /// <param name="f1"><paramref name="f"/>'s side including the item, with the item removed.</param>
        /// <param name="g0"><paramref name="g"/>'s side excluding the item.</param>
        /// <param name="g1"><paramref name="g"/>'s side including the item, with the item removed.</param>
        /// <remarks>
        /// When only one operand's top level equals <paramref name="level"/>, the other operand's
        /// family never mentions that item, so it becomes its own 0-branch with an empty 1-branch.
        /// </remarks>
        public static void Split(
            NodeTable nodes,
            int f,
            int g,
            out int level,
            out int f0,
            out int f1,
            out int g0,
            out int g1)
        {
            Debug.Assert(
                !NodeTable.IsTerminal(f) || !NodeTable.IsTerminal(g),
                "A pair of terminals is always settled by the base case and never reaches Split.");

            int fLevel = NodeTable.IsTerminal(f) ? 0 : nodes[f].Level;
            int gLevel = NodeTable.IsTerminal(g) ? 0 : nodes[g].Level;

            level = Math.Max(fLevel, gLevel);

            if (fLevel == level)
            {
                ref ZddNode node = ref nodes[f];
                f0 = node.Lo;
                f1 = node.Hi;
            }
            else
            {
                f0 = f;
                f1 = NodeTable.Bottom;
            }

            if (gLevel == level)
            {
                ref ZddNode node = ref nodes[g];
                g0 = node.Lo;
                g1 = node.Hi;
            }
            else
            {
                g0 = g;
                g1 = NodeTable.Bottom;
            }
        }
    }
}

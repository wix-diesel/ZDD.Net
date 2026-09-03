using System;
using ZDD.Net.Core;

namespace ZDD.Net.Frontier
{
    /// <summary>
    /// The second frontier pass: turns an unreduced <see cref="TemporaryNodeTable"/> into a
    /// canonical <see cref="Zdd"/> by registering every temporary node through the Core unique
    /// table, from level 1 up to the root.
    /// </summary>
    /// <remarks>
    /// Both ZDD reduction rules fall out of <see cref="UniqueTable.GetNode"/> for free: it already
    /// replaces a <c>Hi == &#8869;</c> node with its <c>Lo</c> child (rule A), and returns the
    /// existing node for a repeated <c>(Level, Lo, Hi)</c> triple instead of a new one (rule B).
    /// So this pass only has to walk the temporary nodes low level first, translating each child
    /// reference to the Core id its own level already produced.
    /// </remarks>
    internal static class BottomUpReducer
    {
        /// <summary>Reduces <paramref name="table"/> into a family owned by <paramref name="manager"/>.</summary>
        /// <param name="manager">The manager whose unique table the nodes are registered into.</param>
        /// <param name="table">The unreduced table produced by a top-down expansion.</param>
        public static Zdd Reduce(ZddManager manager, TemporaryNodeTable table) => Reduce(manager, table, out _);

        /// <summary>
        /// Reduces <paramref name="table"/> as <see cref="Reduce(ZddManager, TemporaryNodeTable)"/>
        /// does, additionally handing back the Core id each temporary node was translated to — the
        /// mapping <see cref="Frontier.FrontierBuilder"/>'s state-recording <c>Build</c> overload needs
        /// to key its recorded labels by the same node ids <see cref="Io.DotWriter"/> works with
        /// (M5-4, issue #56).
        /// </summary>
        /// <param name="manager">The manager whose unique table the nodes are registered into.</param>
        /// <param name="table">The unreduced table produced by a top-down expansion.</param>
        /// <param name="coreIdsByLevel">
        /// Core node ids, indexed like <paramref name="table"/>'s levels and, within a level, like its
        /// node array; a level no branch reached stays null. Empty when <paramref name="table"/> has no
        /// non-terminal level (<see cref="TemporaryNodeTable.RootLevel"/> is 0).
        /// </param>
        public static Zdd Reduce(ZddManager manager, TemporaryNodeTable table, out int[]?[] coreIdsByLevel)
        {
            if (table.RootLevel == 0)
            {
                coreIdsByLevel = Array.Empty<int[]?>();
                return table.Root.IsTop ? manager.Base : manager.Empty;
            }

            UniqueTable core = manager.Table;

            // Core ids of every temporary node, by level; a level no branch reached stays null and
            // is never dereferenced, since nothing among the levels above can reference into it.
            int[]?[] ids = new int[]?[table.RootLevel + 1];

            for (int level = 1; level <= table.RootLevel; level++)
            {
                ReadOnlySpan<TemporaryNode> nodes = table[level];
                int width = nodes.Length;

                if (width == 0)
                {
                    continue;
                }

                int[] coreIds = new int[width];

                for (int index = 0; index < width; index++)
                {
                    TemporaryNode node = nodes[index];
                    int lo = Resolve(ids, node.Lo);
                    int hi = Resolve(ids, node.Hi);
                    coreIds[index] = core.GetNode(level, lo, hi);
                }

                ids[level] = coreIds;
            }

            coreIdsByLevel = ids;
            return new Zdd(manager, Resolve(ids, table.Root));
        }

        /// <summary>Translates one temporary reference into the Core id already produced for it.</summary>
        private static int Resolve(int[]?[] coreIdsByLevel, TemporaryNodeId id)
        {
            if (id.IsTerminal)
            {
                return id.IsTop ? NodeTable.Top : NodeTable.Bottom;
            }

            return coreIdsByLevel[id.Level]![id.Index];
        }
    }
}

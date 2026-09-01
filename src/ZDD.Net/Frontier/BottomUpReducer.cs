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
        public static Zdd Reduce(ZddManager manager, TemporaryNodeTable table)
        {
            if (table.RootLevel == 0)
            {
                return table.Root.IsTop ? manager.Base : manager.Empty;
            }

            UniqueTable core = manager.Table;

            // Core ids of every temporary node, by level; a level no branch reached stays null and
            // is never dereferenced, since nothing among the levels above can reference into it.
            int[][] coreIdsByLevel = new int[table.RootLevel + 1][];

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
                    int lo = Resolve(coreIdsByLevel, node.Lo);
                    int hi = Resolve(coreIdsByLevel, node.Hi);
                    coreIds[index] = core.GetNode(level, lo, hi);
                }

                coreIdsByLevel[level] = coreIds;
            }

            return new Zdd(manager, Resolve(coreIdsByLevel, table.Root));
        }

        /// <summary>Translates one temporary reference into the Core id already produced for it.</summary>
        private static int Resolve(int[][] coreIdsByLevel, TemporaryNodeId id)
        {
            if (id.IsTerminal)
            {
                return id.IsTop ? NodeTable.Top : NodeTable.Bottom;
            }

            return coreIdsByLevel[id.Level][id.Index];
        }
    }
}

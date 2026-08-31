using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZDD.Net.Core;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Writes a family as a Graphviz DOT graph. Backs <see cref="Zdd.ToDot"/> / <see cref="Zdd.WriteDot"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Convention: non-terminal nodes are circles labeled by the branching item (<c>x0</c>,
    /// <c>x1</c>, ...); terminals are boxes (⊥ = ∅, ⊤ = <c>{∅}</c>); the 0-edge is dashed, the
    /// 1-edge is solid; same-item nodes share a rank, root-side ranks first; an unlabeled marker
    /// node points at the root.
    /// </para>
    /// <para>
    /// The traversal is iterative (explicit stack) to avoid stack overflow on deep diagrams.
    /// Output is deterministic: nodes are ordered by (level, id) and lines always use <c>\n</c>.
    /// </para>
    /// </remarks>
    internal static class DotWriter
    {
        /// <summary>Initial depth of the explicit stack; doubles on demand.</summary>
        private const int InitialStackCapacity = 32;

        /// <summary>DOT node name for terminal ⊥. Real nodes use <c>n</c> + id, so no collision.</summary>
        private const string BottomName = "bottom";

        /// <summary>DOT node name for terminal ⊤.</summary>
        private const string TopName = "top";

        /// <summary>DOT node name for the marker that points at the root.</summary>
        private const string RootName = "root";

        /// <summary>Returns the family's DOT representation as a string.</summary>
        /// <param name="zdd">Family to write.</param>
        public static string Write(in Zdd zdd)
        {
            // Newlines come directly from Write(zdd, writer); StringWriter.NewLine is unused.
            using StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            Write(zdd, writer);
            return writer.ToString();
        }

        /// <summary>Writes the family's DOT representation to <paramref name="writer"/>.</summary>
        /// <param name="zdd">Family to write.</param>
        /// <param name="writer">Destination writer.</param>
        public static void Write(in Zdd zdd, TextWriter writer)
        {
            ThrowHelper.ThrowIfNull(writer, nameof(writer));

            ZddManager manager = zdd.Manager;

            // Throws ObjectDisposedException here if disposed.
            NodeTable nodes = manager.Table.Nodes;

            int rootId = zdd.Id;
            int[] ids = CollectByLevel(manager, nodes, rootId, out bool usesBottom, out bool usesTop);

            writer.Write("digraph zdd {\n");
            writer.Write("    graph [rankdir=TB];\n");
            writer.Write("    node [shape=circle, fontname=\"sans-serif\"];\n");
            writer.Write("    edge [fontname=\"sans-serif\"];\n");
            writer.Write('\n');

            WriteNodeDeclarations(writer, manager, nodes, ids, usesBottom, usesTop);
            WriteEdges(writer, nodes, ids, rootId);
            WriteRanks(writer, nodes, ids, usesBottom, usesTop);

            writer.Write("}\n");
        }

        /// <summary>Collects non-terminal nodes reachable from the root, ordered root-side-first then by ascending id, and notes which terminals are reached.</summary>
        private static int[] CollectByLevel(
            ZddManager manager,
            NodeTable nodes,
            int rootId,
            out bool usesBottom,
            out bool usesTop)
        {
            usesBottom = rootId == NodeTable.Bottom;
            usesTop = rootId == NodeTable.Top;

            if (NodeTable.IsTerminal(rootId))
            {
                return Array.Empty<int>();
            }

            HashSet<int> visited = new HashSet<int> { rootId };
            List<int> found = new List<int>();

            int[] stack = new int[InitialStackCapacity];
            int top = 0;
            stack[top++] = rootId;

            while (top > 0)
            {
                int id = stack[--top];
                found.Add(id);

                int lo;
                int hi;
                {
                    ref ZddNode node = ref nodes[id];
                    lo = node.Lo;
                    hi = node.Hi;
                }

                Visit(nodes, visited, ref stack, ref top, lo, ref usesBottom, ref usesTop);
                Visit(nodes, visited, ref stack, ref top, hi, ref usesBottom, ref usesTop);
            }

            int[] ids = found.ToArray();

            // Sort key: root-side depth first, then ascending id — packed into one long each.
            long[] keys = new long[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                long depth = manager.VariableCount - nodes[ids[i]].Level;
                keys[i] = (depth << 32) | (uint)ids[i];
            }

            Array.Sort(keys, ids);
            return ids;
        }

        /// <summary>Visits one child: marks the terminal reached, or pushes an unvisited non-terminal.</summary>
        private static void Visit(
            NodeTable nodes,
            HashSet<int> visited,
            ref int[] stack,
            ref int top,
            int childId,
            ref bool usesBottom,
            ref bool usesTop)
        {
            if (NodeTable.IsTerminal(childId))
            {
                if (childId == NodeTable.Bottom)
                {
                    usesBottom = true;
                }
                else
                {
                    usesTop = true;
                }

                return;
            }

            if (!visited.Add(childId))
            {
                return;
            }

            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = childId;
        }

        private static void WriteNodeDeclarations(
            TextWriter writer,
            ZddManager manager,
            NodeTable nodes,
            int[] ids,
            bool usesBottom,
            bool usesTop)
        {
            writer.Write("    ");
            writer.Write(RootName);
            writer.Write(" [shape=none, label=\"\", width=0, height=0];\n");

            foreach (int id in ids)
            {
                writer.Write("    ");
                WriteName(writer, id);
                writer.Write(" [label=\"x");
                writer.Write(manager.ItemOf(nodes[id].Level).ToString(CultureInfo.InvariantCulture));
                writer.Write("\"];\n");
            }

            if (usesBottom)
            {
                writer.Write("    ");
                writer.Write(BottomName);
                writer.Write(" [shape=box, label=\"⊥\"];\n");
            }

            if (usesTop)
            {
                writer.Write("    ");
                writer.Write(TopName);
                writer.Write(" [shape=box, label=\"⊤\"];\n");
            }

            writer.Write('\n');
        }

        private static void WriteEdges(TextWriter writer, NodeTable nodes, int[] ids, int rootId)
        {
            writer.Write("    ");
            writer.Write(RootName);
            writer.Write(" -> ");
            WriteName(writer, rootId);
            writer.Write(";\n");

            foreach (int id in ids)
            {
                int lo;
                int hi;
                {
                    ref ZddNode node = ref nodes[id];
                    lo = node.Lo;
                    hi = node.Hi;
                }

                // Solid is DOT's default, but state it explicitly to make the pairing visible.
                WriteEdge(writer, id, lo, "dashed");
                WriteEdge(writer, id, hi, "solid");
            }

            writer.Write('\n');
        }

        private static void WriteEdge(TextWriter writer, int from, int to, string style)
        {
            writer.Write("    ");
            WriteName(writer, from);
            writer.Write(" -> ");
            WriteName(writer, to);
            writer.Write(" [style=");
            writer.Write(style);
            writer.Write("];\n");
        }

        /// <summary>Groups same-item nodes with <c>rank=same</c>; terminals are grouped in the last rank.</summary>
        private static void WriteRanks(
            TextWriter writer,
            NodeTable nodes,
            int[] ids,
            bool usesBottom,
            bool usesTop)
        {
            int index = 0;
            while (index < ids.Length)
            {
                int level = nodes[ids[index]].Level;

                writer.Write("    { rank=same;");

                while (index < ids.Length && nodes[ids[index]].Level == level)
                {
                    writer.Write(' ');
                    WriteName(writer, ids[index]);
                    writer.Write(';');
                    index++;
                }

                writer.Write(" }\n");
            }

            if (!usesBottom && !usesTop)
            {
                return;
            }

            writer.Write("    { rank=same;");

            if (usesBottom)
            {
                writer.Write(' ');
                writer.Write(BottomName);
                writer.Write(';');
            }

            if (usesTop)
            {
                writer.Write(' ');
                writer.Write(TopName);
                writer.Write(';');
            }

            writer.Write(" }\n");
        }

        /// <summary>Maps a node id to its DOT node name. Only terminals get fixed names.</summary>
        private static void WriteName(TextWriter writer, int id)
        {
            switch (id)
            {
                case NodeTable.Bottom:
                    writer.Write(BottomName);
                    return;
                case NodeTable.Top:
                    writer.Write(TopName);
                    return;
                default:
                    writer.Write('n');
                    writer.Write(id.ToString(CultureInfo.InvariantCulture));
                    return;
            }
        }
    }
}

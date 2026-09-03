using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZDD.Net.Core;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Writes a family as a Graphviz DOT graph. Backs <see cref="Zdd.ToDot(DotOptions?)"/> /
    /// <see cref="Zdd.WriteDot(TextWriter, DotOptions?)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Convention: non-terminal nodes are circles labeled by the branching item (<c>x0</c>,
    /// <c>x1</c>, ...); terminals are boxes (⊥ = ∅, ⊤ = <c>{∅}</c>); the 0-edge is dashed, the
    /// 1-edge is solid; same-item nodes share a rank, root-side ranks first; an unlabeled marker
    /// node points at the root. <see cref="DotOptions"/> can replace the item label with a
    /// meaningful name, attach a spec-state label to each node, cap what is drawn (top levels,
    /// node count, or a specific node's reachable part only, replacing the rest with a single
    /// truncation marker), and restyle nodes/edges — see its members. A freshly constructed
    /// <see cref="DotOptions"/> (or <see langword="null"/>) reproduces this default rendering exactly.
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

        /// <summary>DOT node name for the marker that replaces everything a <see cref="DotOptions"/> cutoff excluded.</summary>
        private const string TruncatedName = "truncated";

        /// <summary>Shared, never-mutated stand-in for a <see langword="null"/> <see cref="DotOptions"/>.</summary>
        private static readonly DotOptions DefaultOptions = new DotOptions();

        /// <summary>Returns the family's DOT representation as a string.</summary>
        /// <param name="zdd">Family to write.</param>
        /// <param name="options">Rendering knobs; defaults when <see langword="null"/>.</param>
        public static string Write(in Zdd zdd, DotOptions? options = null)
        {
            // Newlines come directly from Write(zdd, writer, options); StringWriter.NewLine is unused.
            using StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            Write(zdd, writer, options);
            return writer.ToString();
        }

        /// <summary>Writes the family's DOT representation to <paramref name="writer"/>.</summary>
        /// <param name="zdd">Family to write.</param>
        /// <param name="writer">Destination writer.</param>
        /// <param name="options">Rendering knobs; defaults when <see langword="null"/>.</param>
        public static void Write(in Zdd zdd, TextWriter writer, DotOptions? options = null)
        {
            ThrowHelper.ThrowIfNull(writer, nameof(writer));

            DotOptions effective = options ?? DefaultOptions;
            ZddManager manager = zdd.Manager;

            // Throws ObjectDisposedException here if disposed.
            NodeTable nodes = manager.Table.Nodes;

            int rootId = effective.FocusNodeId ?? zdd.Id;
            int[] ids = CollectByLevel(nodes, rootId, effective, out bool usesBottom, out bool usesTop);

            HashSet<int> admitted = new HashSet<int>(ids);
            bool truncated = IsTruncated(nodes, ids, admitted);

            writer.Write("digraph zdd {\n");
            writer.Write("    graph [rankdir=TB];\n");
            writer.Write("    node [shape=");
            writer.Write(effective.NonTerminalShape);

            if (effective.NonTerminalColor is not null)
            {
                writer.Write(", style=filled, fillcolor=\"");
                WriteEscaped(writer, effective.NonTerminalColor);
                writer.Write('"');
            }

            writer.Write(", fontname=\"sans-serif\"];\n");
            writer.Write("    edge [fontname=\"sans-serif\"];\n");
            writer.Write('\n');

            WriteNodeDeclarations(writer, manager, nodes, ids, usesBottom, usesTop, truncated, effective);
            WriteEdges(writer, nodes, ids, rootId, admitted, effective);
            WriteRanks(writer, nodes, ids, usesBottom, usesTop);

            writer.Write("}\n");
        }

        /// <summary>
        /// Collects the non-terminal nodes to draw, ordered root-side-first then by ascending id,
        /// noting which terminals are reached. Stops admitting a node once it falls below
        /// <see cref="DotOptions.MaxLevels"/> (relative to <paramref name="rootId"/>'s own level) or
        /// once <see cref="DotOptions.MaxNodes"/> are already admitted — the cutoffs bound the walk
        /// itself, not just its output, so a huge diagram never has more than <see cref="DotOptions.MaxNodes"/>
        /// nodes' worth of work done on it.
        /// </summary>
        private static int[] CollectByLevel(
            NodeTable nodes,
            int rootId,
            DotOptions options,
            out bool usesBottom,
            out bool usesTop)
        {
            usesBottom = rootId == NodeTable.Bottom;
            usesTop = rootId == NodeTable.Top;

            if (NodeTable.IsTerminal(rootId))
            {
                return Array.Empty<int>();
            }

            int rootLevel = nodes[rootId].Level;
            int minLevel = options.MaxLevels >= rootLevel ? 1 : rootLevel - options.MaxLevels + 1;
            int maxNodes = options.MaxNodes;

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

                Visit(nodes, visited, minLevel, maxNodes, ref stack, ref top, lo, ref usesBottom, ref usesTop);
                Visit(nodes, visited, minLevel, maxNodes, ref stack, ref top, hi, ref usesBottom, ref usesTop);
            }

            int[] ids = found.ToArray();

            // Sort key: root-side depth first, then ascending id — packed into one long each.
            long[] keys = new long[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                long depth = rootLevel - nodes[ids[i]].Level;
                keys[i] = (depth << 32) | (uint)ids[i];
            }

            Array.Sort(keys, ids);
            return ids;
        }

        /// <summary>Visits one child: marks the terminal reached, or pushes an unvisited, in-bounds non-terminal.</summary>
        private static void Visit(
            NodeTable nodes,
            HashSet<int> visited,
            int minLevel,
            int maxNodes,
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

            if (visited.Contains(childId))
            {
                return;
            }

            // Beyond a DotOptions cutoff: leave it unvisited. WriteEdges redirects the edge that
            // would have reached it to the truncation marker instead.
            if (visited.Count >= maxNodes || nodes[childId].Level < minLevel)
            {
                return;
            }

            visited.Add(childId);

            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = childId;
        }

        /// <summary>Whether some drawn node's child was excluded by a <see cref="DotOptions"/> cutoff.</summary>
        private static bool IsTruncated(NodeTable nodes, int[] ids, HashSet<int> admitted)
        {
            foreach (int id in ids)
            {
                ref ZddNode node = ref nodes[id];

                if (IsExcluded(node.Lo, admitted) || IsExcluded(node.Hi, admitted))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsExcluded(int childId, HashSet<int> admitted) =>
            !NodeTable.IsTerminal(childId) && !admitted.Contains(childId);

        private static void WriteNodeDeclarations(
            TextWriter writer,
            ZddManager manager,
            NodeTable nodes,
            int[] ids,
            bool usesBottom,
            bool usesTop,
            bool truncated,
            DotOptions options)
        {
            writer.Write("    ");
            writer.Write(RootName);
            writer.Write(" [shape=none, label=\"\", width=0, height=0];\n");

            foreach (int id in ids)
            {
                int item = manager.ItemOf(nodes[id].Level);

                writer.Write("    ");
                WriteName(writer, id);
                writer.Write(" [label=\"");
                WriteLevelLabel(writer, item, options.LevelLabel);

                if (options.StateLabels is not null && options.StateLabels.TryGetValue(id, out string? stateLabel))
                {
                    writer.Write("\\n");
                    WriteEscaped(writer, stateLabel);
                }

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

            if (truncated)
            {
                writer.Write("    ");
                writer.Write(TruncatedName);
                writer.Write(" [shape=box, style=dashed, label=\"…\"];\n");
            }

            writer.Write('\n');
        }

        /// <summary>Writes an item's level label: <c>x&lt;item&gt;</c>, or <see cref="DotOptions.LevelLabel"/>'s result, escaped.</summary>
        private static void WriteLevelLabel(TextWriter writer, int item, Func<int, string>? levelLabel)
        {
            if (levelLabel is null)
            {
                writer.Write('x');
                writer.Write(item.ToString(CultureInfo.InvariantCulture));
                return;
            }

            WriteEscaped(writer, levelLabel(item));
        }

        private static void WriteEdges(
            TextWriter writer,
            NodeTable nodes,
            int[] ids,
            int rootId,
            HashSet<int> admitted,
            DotOptions options)
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
                WriteEdge(writer, id, lo, options.ZeroEdgeStyle, admitted);
                WriteEdge(writer, id, hi, options.OneEdgeStyle, admitted);
            }

            writer.Write('\n');
        }

        private static void WriteEdge(TextWriter writer, int from, int to, string style, HashSet<int> admitted)
        {
            writer.Write("    ");
            WriteName(writer, from);
            writer.Write(" -> ");

            if (IsExcluded(to, admitted))
            {
                writer.Write(TruncatedName);
            }
            else
            {
                WriteName(writer, to);
            }

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

        /// <summary>Writes <paramref name="text"/> into a DOT quoted string, escaping <c>"</c>, <c>\</c> and newlines.</summary>
        private static void WriteEscaped(TextWriter writer, string text)
        {
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"':
                        writer.Write("\\\"");
                        break;
                    case '\\':
                        writer.Write("\\\\");
                        break;
                    case '\n':
                        writer.Write("\\n");
                        break;
                    case '\r':
                        // Dropped, matching the rest of the output's \n-only convention.
                        break;
                    default:
                        writer.Write(c);
                        break;
                }
            }
        }
    }
}

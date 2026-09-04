using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ZDD.Net.Core;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Reads and writes a <see cref="Zdd"/> in the text format used by Python
    /// <a href="https://github.com/graphillion/graphillion">Graphillion</a>'s
    /// <c>setset.dump</c>/<c>dumps</c>/<c>load</c>/<c>loads</c> (in turn SAPPOROBDD's ZDD export
    /// format), so a family built in one library can be handed to the other &#8212; for migrating
    /// existing Graphillion assets, and for cross-checking this library's results against an
    /// independent implementation (docs/PLAN.md &#167;9).
    /// </summary>
    /// <example>
    /// <code>
    /// string dump = GraphillionTextFormat.Write(family); // hand this string to Python's setset.loads()
    /// Zdd reloaded = GraphillionTextFormat.Read(dump, variableCount: 5);
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// <b>Format, reverse-engineered from real output.</b> No public specification of this format
    /// exists; the layout below was determined by installing Graphillion 2.1 from PyPI, dumping
    /// families built from known inputs, and cross-referencing
    /// <c>src/graphillion/zdd.cc</c>'s <c>dump</c>/<c>load</c> functions in Graphillion's own
    /// source (not by guessing). It is plain ASCII text, one line per node:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// The trivial families ⊥ (empty family) and ⊤ (<c>{∅}</c>) are written as a single line,
    /// <c>B</c> or <c>T</c>, followed by a terminating <c>.</c> line.
    /// </description></item>
    /// <item><description>
    /// Any other family is one line per reachable non-terminal node, each
    /// <c>&lt;id&gt; &lt;elem&gt; &lt;lo&gt; &lt;hi&gt;</c> (whitespace-separated), followed by a
    /// terminating <c>.</c> line. <c>id</c> is an opaque integer used only to cross-reference
    /// within the file (Graphillion's own writer uses its internal SAPPOROBDD node ids; this
    /// writer reuses this library's own node ids, which work equally well as opaque tokens).
    /// <c>lo</c>/<c>hi</c> are either <c>B</c>/<c>T</c> for a terminal child, or an earlier line's
    /// <c>id</c>. Nodes are written in dependency order (every child line precedes the line that
    /// references it), which also always places the root on the <b>last</b> line before the
    /// <c>.</c> &#8212; that is how <see cref="Read(TextReader,int?,ZddManagerOptions?)"/> knows
    /// which node is the root, since the format never says so explicitly (this is exactly how
    /// Graphillion's own loader identifies the root, too: it just keeps the last node it built).
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Level direction.</b> This is the one place a mismatch would silently produce a family
    /// that "reads fine but is upside down". Graphillion's <c>elem</c> is <b>1-based and counts
    /// from the root side</b> (the first variable tested walking down from the root is elem 1;
    /// the variable adjacent to the terminals has the largest elem). This library's public,
    /// 0-based <see cref="ZddManager"/> <i>item</i> index uses the very same direction (item 0 is
    /// also the first variable tested from the root &#8212; see <see cref="ZddManager"/>'s remarks),
    /// so the correspondence is just the 0-based/1-based offset: <c>elem = item + 1</c> (the
    /// internal, leaf-side-up <c>Level</c> field is not part of this correspondence at all; it
    /// only shows up inside this class as an implementation detail of <see cref="ZddManager.LevelOf"/>/
    /// <see cref="ZddManager.ItemOf"/>). Concretely: <c>Write</c> emits <c>manager.ItemOf(node.Level) + 1</c>
    /// as a node's <c>elem</c>; <c>Read</c> turns a line's <c>elem</c> back into a level via
    /// <c>manager.LevelOf(elem - 1)</c>. A family that is not symmetric under reversing its
    /// variable order (e.g. an s&#8211;t path family on an asymmetric graph) is what actually
    /// exercises this: a family that happens to be symmetric would round-trip correctly even with
    /// the direction backwards, silently hiding the bug.
    /// </para>
    /// <para>
    /// <b>No variable count in the file.</b> Unlike <see cref="ZddBinaryFormat"/>, Graphillion's
    /// format has no header and does not record how many variables the universe has &#8212; only
    /// the largest <c>elem</c> that happens to appear in a dumped family, which can be smaller than
    /// the true universe size if the family never uses the highest-numbered variables. Pass
    /// <c>variableCount</c> explicitly (e.g. matching a Graphillion
    /// <c>GraphSet.set_universe(edges)</c> call's edge count) when the two need to agree, such as
    /// when combining the result with families built elsewhere with a specific variable count, or
    /// when comparing enumerated item-index sets against another library's output one to one;
    /// leaving it <see langword="null"/> infers the smallest count the file's own <c>elem</c>
    /// values need.
    /// </para>
    /// <para>
    /// <b>Corrupt input.</b> A missing/unterminated dump, trailing content after the terminating
    /// <c>.</c> line (including after a bare <c>B</c>/<c>T</c> dump, which this format always
    /// still terminates with <c>.</c> even though Graphillion's own loader does not bother
    /// checking for one there), a malformed node line, a node id defined more than once, an
    /// <c>elem</c> outside <c>1..variableCount</c> (including one that does not fit an explicitly
    /// supplied <c>variableCount</c> &#8212; the "format cannot represent this" case), a
    /// <c>lo</c>/<c>hi</c> reference to an id that was not defined by an earlier line, or a
    /// <c>hi</c> equal to the bottom terminal (impossible for any node a real node table ever
    /// held, per the zero-suppression rule) all throw <see cref="ZddFormatException"/> rather than
    /// crashing or silently building the wrong family.
    /// </para>
    /// </remarks>
    public static class GraphillionTextFormat
    {
        private const string BottomToken = "B";
        private const string TopToken = "T";
        private const string TerminatorToken = ".";

        /// <summary>Initial depth of the explicit stack used to walk reachable nodes; doubles on demand.</summary>
        private const int InitialStackCapacity = 32;

        // ---- Write ----

        /// <summary>Returns <paramref name="zdd"/>'s Graphillion-compatible text representation.</summary>
        /// <param name="zdd">The family to write.</param>
        /// <exception cref="InvalidOperationException"><paramref name="zdd"/> is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public static string Write(in Zdd zdd)
        {
            using StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
            Write(zdd, writer);
            return writer.ToString();
        }

        /// <summary>Writes <paramref name="zdd"/>'s Graphillion-compatible text representation to <paramref name="writer"/>.</summary>
        /// <param name="zdd">The family to write.</param>
        /// <param name="writer">The destination.</param>
        /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="zdd"/> is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public static void Write(in Zdd zdd, TextWriter writer)
        {
            ThrowHelper.ThrowIfNull(writer, nameof(writer));

            ZddManager manager = zdd.Manager;
            int rootId = zdd.Id;

            // Fetched unconditionally (even for the trivial B/T branches below) so a disposed
            // manager throws ObjectDisposedException here regardless of which family it is.
            NodeTable nodes = manager.Table.Nodes;

            if (rootId == NodeTable.Bottom)
            {
                writer.Write(BottomToken);
                writer.Write('\n');
                writer.Write(TerminatorToken);
                writer.Write('\n');
                return;
            }

            if (rootId == NodeTable.Top)
            {
                writer.Write(TopToken);
                writer.Write('\n');
                writer.Write(TerminatorToken);
                writer.Write('\n');
                return;
            }

            int[] ids = CollectReachable(nodes, rootId);

            // A child's id is always strictly less than its parent's (the unique table requires
            // both children to already exist before a node can reference them), so ascending id
            // order is a valid dependency order for the whole reachable set, and places the root
            // — necessarily the largest id among nodes reachable from itself — last.
            Array.Sort(ids);

            foreach (int id in ids)
            {
                ref ZddNode node = ref nodes[id];
                int elem = manager.ItemOf(node.Level) + 1;

                writer.Write(id.ToString(CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write(elem.ToString(CultureInfo.InvariantCulture));
                writer.Write(' ');
                WriteChildToken(writer, node.Lo);
                writer.Write(' ');
                WriteChildToken(writer, node.Hi);
                writer.Write('\n');
            }

            writer.Write(TerminatorToken);
            writer.Write('\n');
        }

        private static void WriteChildToken(TextWriter writer, int childId)
        {
            switch (childId)
            {
                case NodeTable.Bottom:
                    writer.Write(BottomToken);
                    return;
                case NodeTable.Top:
                    writer.Write(TopToken);
                    return;
                default:
                    writer.Write(childId.ToString(CultureInfo.InvariantCulture));
                    return;
            }
        }

        /// <summary>Iteratively (no recursion) collects the non-terminal nodes reachable from <paramref name="rootId"/>.</summary>
        private static int[] CollectReachable(NodeTable nodes, int rootId)
        {
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

                PushIfUnvisited(nodes, visited, ref stack, ref top, lo);
                PushIfUnvisited(nodes, visited, ref stack, ref top, hi);
            }

            return found.ToArray();
        }

        private static void PushIfUnvisited(NodeTable nodes, HashSet<int> visited, ref int[] stack, ref int top, int childId)
        {
            if (NodeTable.IsTerminal(childId) || !visited.Add(childId))
            {
                return;
            }

            if (top == stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[top++] = childId;
        }

        // ---- Read ----

        /// <summary>Reads a family from its Graphillion-compatible text representation. Convenience wrapper around <see cref="Read(TextReader,int?,ZddManagerOptions?)"/>.</summary>
        /// <param name="text">The dump text.</param>
        /// <param name="variableCount">See <see cref="Read(TextReader,int?,ZddManagerOptions?)"/>.</param>
        /// <param name="options">See <see cref="Read(TextReader,int?,ZddManagerOptions?)"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
        /// <exception cref="ZddFormatException">See <see cref="Read(TextReader,int?,ZddManagerOptions?)"/>.</exception>
        public static Zdd Read(string text, int? variableCount = null, ZddManagerOptions? options = null)
        {
            ThrowHelper.ThrowIfNull(text, nameof(text));

            using StringReader reader = new StringReader(text);
            return Read(reader, variableCount, options);
        }

        /// <summary>Reads a family previously written by Graphillion's <c>setset.dump</c>/<c>dumps</c> (or by <see cref="Write(in Zdd,TextWriter)"/>).</summary>
        /// <param name="reader">The source text.</param>
        /// <param name="variableCount">
        /// The new manager's variable count. <see langword="null"/> infers the smallest count the
        /// file's own <c>elem</c> values need (0 for a bare <c>B</c>/<c>T</c> dump); pass an
        /// explicit value to match a specific universe instead &#8212; see the "No variable count
        /// in the file" remarks on <see cref="GraphillionTextFormat"/>.
        /// </param>
        /// <param name="options">
        /// Tuning for the new manager; <see langword="null"/> sizes the node and unique tables from
        /// the file's node count, which is usually what's wanted.
        /// </param>
        /// <returns>The root family, owned by a newly created <see cref="ZddManager"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="variableCount"/> is negative.</exception>
        /// <exception cref="ZddFormatException">See the "Corrupt input" remarks on <see cref="GraphillionTextFormat"/>.</exception>
        public static Zdd Read(TextReader reader, int? variableCount = null, ZddManagerOptions? options = null)
        {
            ThrowHelper.ThrowIfNull(reader, nameof(reader));

            if (variableCount is < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(
                    nameof(variableCount),
                    $"'{nameof(variableCount)}' must not be negative, but was {variableCount}.");
            }

            int lineNumber = 0;
            string? line = ReadNonBlankLine(reader, ref lineNumber);
            if (line is null)
            {
                throw new ZddFormatException("Empty input: expected a Graphillion-format ZDD dump.");
            }

            string trimmed = line.Trim();
            if (trimmed == BottomToken || trimmed == TopToken)
            {
                RequireTerminatorThenNoTrailingContent(reader, ref lineNumber, trimmed);

                ZddManager trivialManager = new ZddManager(variableCount ?? 0, options ?? new ZddManagerOptions());
                return trimmed == BottomToken ? trivialManager.Empty : trivialManager.Base;
            }

            List<(long RawId, int Elem, string Lo, string Hi)> entries = new List<(long, int, string, string)>();
            int maxElem = 0;
            bool terminated = false;

            while (true)
            {
                string trimmedLine = line!.Trim();

                if (trimmedLine == TerminatorToken)
                {
                    terminated = true;
                    break;
                }

                entries.Add(ParseNodeLine(trimmedLine, lineNumber));
                maxElem = Math.Max(maxElem, entries[entries.Count - 1].Elem);

                line = ReadNonBlankLine(reader, ref lineNumber);
                if (line is null)
                {
                    break;
                }
            }

            if (!terminated)
            {
                throw new ZddFormatException("Unexpected end of stream: the dump has no terminating '.' line.");
            }

            RejectTrailingContent(reader, ref lineNumber);

            if (entries.Count == 0)
            {
                throw new ZddFormatException(
                    "The dump has a terminating '.' line but no 'B', 'T', or node lines before it, so no root can be determined.");
            }

            int effectiveVariableCount;
            if (variableCount is int explicitCount)
            {
                if (maxElem > explicitCount)
                {
                    throw new ZddFormatException(
                        $"The dump uses variable(s) up to elem {maxElem}, which does not fit the supplied " +
                        $"variableCount {explicitCount}.");
                }

                effectiveVariableCount = explicitCount;
            }
            else
            {
                effectiveVariableCount = maxElem;
            }

            ZddManager manager = new ZddManager(effectiveVariableCount, EffectiveOptions(options, entries.Count));
            UniqueTable table = manager.Table;

            Dictionary<long, int> idMap = new Dictionary<long, int>(entries.Count);
            int rootId = NodeTable.Bottom;

            for (int i = 0; i < entries.Count; i++)
            {
                (long rawId, int elem, string loToken, string hiToken) = entries[i];

                if (idMap.ContainsKey(rawId))
                {
                    throw new ZddFormatException($"Node id {rawId} is defined more than once in the dump.");
                }

                // elem is already known to be within 1..maxElem <= effectiveVariableCount.
                int level = manager.LevelOf(elem - 1);

                int lo = ResolveReference(loToken, idMap, rawId, "lo");
                int hi = ResolveReference(hiToken, idMap, rawId, "hi");

                if (hi == NodeTable.Bottom)
                {
                    throw new ZddFormatException(
                        $"Node {rawId}: the 'hi' child must not be the bottom terminal (a real ZDD node table " +
                        "never holds one, per the zero-suppression rule).");
                }

                int loLevel = NodeTable.IsTerminal(lo) ? 0 : table.Nodes[lo].Level;
                int hiLevel = NodeTable.IsTerminal(hi) ? 0 : table.Nodes[hi].Level;
                if (level <= loLevel || level <= hiLevel)
                {
                    throw new ZddFormatException(
                        $"Node {rawId}: elem {elem} must be strictly closer to the root than its children's.");
                }

                rootId = table.GetNode(level, lo, hi);
                idMap[rawId] = rootId;
            }

            return new Zdd(manager, rootId);
        }

        /// <summary>Parses one non-terminator, non-blank dump line into its four whitespace-separated fields.</summary>
        /// <exception cref="ZddFormatException">The line does not have exactly 4 fields, or <c>id</c>/<c>elem</c> do not parse as expected.</exception>
        private static (long RawId, int Elem, string Lo, string Hi) ParseNodeLine(string trimmedLine, int lineNumber)
        {
            string[] tokens = trimmedLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != 4)
            {
                throw new ZddFormatException(
                    $"Line {lineNumber}: expected 'id elem lo hi', but found {tokens.Length} field(s): '{trimmedLine}'.");
            }

            if (!long.TryParse(tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long rawId))
            {
                throw new ZddFormatException($"Line {lineNumber}: expected an integer node id, but found '{tokens[0]}'.");
            }

            if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int elem) || elem < 1)
            {
                throw new ZddFormatException($"Line {lineNumber}: expected a positive integer elem, but found '{tokens[1]}'.");
            }

            return (rawId, elem, tokens[2], tokens[3]);
        }

        /// <summary>Translates a raw file-space child reference ('B', 'T', or an earlier line's id) to the id the fresh manager actually assigned it.</summary>
        /// <exception cref="ZddFormatException"><paramref name="token"/> does not name a terminal or an already-defined earlier node.</exception>
        private static int ResolveReference(string token, Dictionary<long, int> idMap, long owningRawId, string which)
        {
            if (token == BottomToken)
            {
                return NodeTable.Bottom;
            }

            if (token == TopToken)
            {
                return NodeTable.Top;
            }

            if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long rawRef)
                || !idMap.TryGetValue(rawRef, out int id))
            {
                throw new ZddFormatException(
                    $"Node {owningRawId}: '{which}' child '{token}' is not 'B', 'T', or an id defined by an earlier line.");
            }

            return id;
        }

        /// <summary>
        /// Consumes the mandatory <c>.</c> line right after a bare <c>B</c>/<c>T</c> dump, then
        /// requires the stream to be exhausted. Unlike Graphillion's own loader (which stops
        /// reading immediately after a bare <c>B</c>/<c>T</c> line and never even looks for a
        /// terminator), this format's own documented shape always ends in <c>.</c> — enforcing it
        /// here is what lets a concatenated or truncated file be rejected instead of silently
        /// accepted.
        /// </summary>
        /// <exception cref="ZddFormatException">No terminator follows, or content follows the terminator.</exception>
        private static void RequireTerminatorThenNoTrailingContent(TextReader reader, ref int lineNumber, string precedingToken)
        {
            string? next = ReadNonBlankLine(reader, ref lineNumber);
            if (next is null || next.Trim() != TerminatorToken)
            {
                throw new ZddFormatException(
                    next is null
                        ? $"Unexpected end of stream: expected a terminating '.' line right after '{precedingToken}'."
                        : $"Line {lineNumber}: expected a terminating '.' line right after '{precedingToken}', but found '{next.Trim()}'.");
            }

            RejectTrailingContent(reader, ref lineNumber);
        }

        /// <summary>Throws if the stream has any more non-blank content (used right after a dump's terminating <c>.</c> line).</summary>
        /// <exception cref="ZddFormatException">The stream has more non-blank content.</exception>
        private static void RejectTrailingContent(TextReader reader, ref int lineNumber)
        {
            string? trailing = ReadNonBlankLine(reader, ref lineNumber);
            if (trailing is not null)
            {
                throw new ZddFormatException(
                    $"Line {lineNumber}: unexpected content after the dump's terminating '.' line: '{trailing.Trim()}'.");
            }
        }

        /// <summary>Reads lines, skipping blank/whitespace-only ones, tracking a 1-based line number for diagnostics.</summary>
        private static string? ReadNonBlankLine(TextReader reader, ref int lineNumber)
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                lineNumber++;
                if (line.Trim().Length != 0)
                {
                    return line;
                }
            }

            return null;
        }

        /// <summary>Sizes a fresh manager's tables from the file's node count, unless the caller already opted in to specific tuning.</summary>
        private static ZddManagerOptions EffectiveOptions(ZddManagerOptions? options, int nodeCount)
        {
            if (options is not null || nodeCount == 0)
            {
                return options ?? new ZddManagerOptions();
            }

            ZddManagerOptions tuned = new ZddManagerOptions
            {
                InitialNodeCapacity = nodeCount,
                InitialUniqueTableCapacity = Math.Min(nodeCount, UniqueTable.MaxCapacity),
            };

            return tuned;
        }
    }
}

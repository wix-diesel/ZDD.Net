using System;
using System.Buffers.Binary;
using System.IO;
using ZDD.Net.Core;
using ZDD.Net.Internal;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Reads and writes a <see cref="Zdd"/> in ZDD.Net's own binary format: the internal node array
    /// (<c>Level</c>/<c>Lo</c>/<c>Hi</c>), written out with almost no transformation, so it is far
    /// faster and more compact than a text format (docs/PLAN.md &#167;9).
    /// </summary>
    /// <example>
    /// <code>
    /// using ZddManager manager = new ZddManager(variableCount: 5);
    /// Zdd family = manager.Singleton(0) | manager.Singleton(1); // {{0}, {1}}
    ///
    /// using (FileStream stream = File.Create("family.zdd"))
    /// {
    ///     ZddBinaryFormat.Write(family, stream);
    /// }
    ///
    /// using (FileStream stream = File.OpenRead("family.zdd"))
    /// {
    ///     Zdd reloaded = ZddBinaryFormat.Read(stream);
    ///     Console.WriteLine(reloaded.Count == family.Count); // true
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// <b>Layout.</b> A 21-byte fixed header, then one entry per node:
    /// </para>
    /// <list type="table">
    /// <item><description><c>Magic</c>: 4 bytes, ASCII <c>"ZDDB"</c>.</description></item>
    /// <item><description><c>FormatVersion</c>: 4-byte little-endian <c>uint</c> (currently <see cref="FormatVersion"/>).</description></item>
    /// <item><description><c>Endianness</c>: 1 byte, always <c>0</c> (little-endian on disk; multi-byte
    /// fields are written with <see cref="BinaryPrimitives"/>, which is endian-correct regardless of the
    /// host's own byte order, so this is a format-level declaration rather than something the host needs
    /// to act on).</description></item>
    /// <item><description><c>VariableCount</c>: 4-byte little-endian <c>int</c>.</description></item>
    /// <item><description><c>NodeCount</c>: 4-byte little-endian <c>int</c> &#8212; the number of
    /// non-terminal nodes that follow.</description></item>
    /// <item><description><c>RootId</c>: 4-byte little-endian <c>int</c> &#8212; <c>0</c>/<c>1</c> for
    /// the terminals, or a reference into the node table below.</description></item>
    /// <item><description>Then <c>NodeCount</c> node entries, each three <see cref="VarInt"/>-encoded
    /// fields: <c>Level</c>, <c>Lo</c>, <c>Hi</c>.</description></item>
    /// </list>
    /// <para>
    /// <b>What gets written.</b> <see cref="Write"/> serializes the <i>entire</i> node table of the
    /// family's manager, not just the nodes reachable from the family being written &#8212; the table has
    /// no gaps (no node GC exists yet; see M5-3), so every id in <c>2 .. NodeCount + 1</c> is a real node,
    /// and dumping it as-is is the "almost no transformation" fast path. For a manager used to build
    /// exactly one family before saving (the common case), this also makes <b>node IDs round-trip
    /// exactly</b>: see below.
    /// </para>
    /// <para>
    /// <b>Canonicity on read.</b> <see cref="Read"/> replays each node through
    /// <see cref="UniqueTable.GetNode"/> on a fresh <see cref="ZddManager"/> (docs/PLAN.md &#167;9's
    /// "re-register in the unique table" option) rather than restoring the raw array directly. Node ids
    /// are always assigned in creation order (a node's <c>Lo</c>/<c>Hi</c> must already exist when it is
    /// created &#8212; enforced by <see cref="NodeTable.Add"/>), so the file's node order is already a
    /// valid replay order. Replaying through the unique table means: (a) a node ID from the file maps to
    /// whatever ID the fresh manager actually assigns it, tracked in a table that every later reference is
    /// translated through, so this stays correct even if a corrupt file describes the same
    /// <c>(Level, Lo, Hi)</c> triple twice; and (b) since a fresh manager's unique table starts empty, a
    /// file with no such duplicates reproduces the exact original IDs &#8212; the round-trip test this
    /// format is built against.
    /// </para>
    /// <para>
    /// <b>Corrupt input.</b> A truncated stream, bad magic/endianness, a <c>Lo</c>/<c>Hi</c> reference
    /// that is not an earlier node in the file, a level outside <c>1 .. VariableCount</c> or not strictly
    /// above its children's levels, or a <c>Hi</c> equal to the bottom terminal (impossible for any node a
    /// real <see cref="NodeTable"/> ever held, since <see cref="NodeTable.Add"/> rejects it) all throw
    /// <see cref="ZddFormatException"/> rather than corrupting the manager or crashing.
    /// </para>
    /// <para>
    /// <b>Versioning.</b> <see cref="FormatVersion"/> is 1; there is no version 0. The policy going
    /// forward is that every future version of this library keeps the ability to read version 1 files
    /// (and every version in between) &#8212; <see cref="Read"/> should switch on the version field and
    /// dispatch to per-version parsing rather than assuming today's fixed layout. A file whose version
    /// this build does not recognize (too new) throws <see cref="ZddFormatException"/> naming both the
    /// file's version and the version(s) this build supports.
    /// </para>
    /// </remarks>
    public static class ZddBinaryFormat
    {
        /// <summary>The format version this build writes, and the newest version it can read.</summary>
        public const uint FormatVersion = 1;

        private const int MagicSize = 4;
        private const int HeaderSize = MagicSize + 4 + 1 + 4 + 4 + 4;
        private const byte LittleEndianFlag = 0;

        /// <summary>I/O buffer size for the <see cref="BufferedStream"/> wrapping the caller's stream.</summary>
        private const int BufferSize = 64 * 1024;

        private static ReadOnlySpan<byte> Magic => "ZDDB"u8;

        /// <summary>Writes <paramref name="zdd"/>'s manager's entire node table to <paramref name="stream"/>.</summary>
        /// <param name="zdd">The family to write; its <see cref="Zdd.Manager"/> supplies the node table (see remarks on <see cref="ZddBinaryFormat"/>).</param>
        /// <param name="stream">The destination stream; left open (not disposed) but flushed before returning.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="zdd"/> is <c>default(Zdd)</c>.</exception>
        /// <exception cref="ObjectDisposedException">The owning manager has been disposed.</exception>
        public static void Write(in Zdd zdd, Stream stream)
        {
            ThrowHelper.ThrowIfNull(stream, nameof(stream));

            ZddManager manager = zdd.Manager;
            NodeTable nodeTable = manager.Table.Nodes;
            int nodeCount = nodeTable.Count;
            int rootId = zdd.Id;

            BufferedStream buffered = new BufferedStream(stream, BufferSize);

            Span<byte> header = stackalloc byte[HeaderSize];
            Magic.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(MagicSize, 4), FormatVersion);
            header[MagicSize + 4] = LittleEndianFlag;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(MagicSize + 5, 4), manager.VariableCount);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(MagicSize + 9, 4), nodeCount);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(MagicSize + 13, 4), rootId);
            buffered.Write(header);

            for (int id = NodeTable.FirstNodeId; id < NodeTable.FirstNodeId + nodeCount; id++)
            {
                ref ZddNode node = ref nodeTable[id];
                VarInt.WriteUInt32(buffered, (uint)node.Level);
                VarInt.WriteUInt32(buffered, (uint)node.Lo);
                VarInt.WriteUInt32(buffered, (uint)node.Hi);
            }

            buffered.Flush();
        }

        /// <summary>Reads a family previously written by <see cref="Write"/>, rebuilding it in a fresh <see cref="ZddManager"/>.</summary>
        /// <param name="stream">The source stream.</param>
        /// <param name="options">
        /// Tuning for the new manager; <see langword="null"/> sizes the node and unique tables from the
        /// file's declared node count (avoiding repeated table growth while loading a large ZDD), which is
        /// usually what's wanted &#8212; pass an explicit instance to opt out.
        /// </param>
        /// <returns>The root family, owned by a newly created <see cref="ZddManager"/> (reachable via <see cref="Zdd.Manager"/>).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
        /// <exception cref="ZddFormatException">See the "Corrupt input" and "Versioning" remarks on <see cref="ZddBinaryFormat"/>.</exception>
        public static Zdd Read(Stream stream, ZddManagerOptions? options = null)
        {
            ThrowHelper.ThrowIfNull(stream, nameof(stream));

            BufferedStream buffered = new BufferedStream(stream, BufferSize);

            Span<byte> magicBuffer = stackalloc byte[MagicSize];
            ReadExact(buffered, magicBuffer, "magic number");
            if (!magicBuffer.SequenceEqual(Magic))
            {
                throw new ZddFormatException("Not a ZDD.Net binary file: the magic number does not match 'ZDDB'.");
            }

            Span<byte> intBuffer = stackalloc byte[4];

            ReadExact(buffered, intBuffer, "format version");
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(intBuffer);
            if (version != FormatVersion)
            {
                throw new ZddFormatException(
                    $"Unsupported format version {version}; this build of ZDD.Net reads format version {FormatVersion}.");
            }

            int endianness = buffered.ReadByte();
            if (endianness < 0)
            {
                throw new ZddFormatException("Unexpected end of stream while reading the endianness flag.");
            }

            if (endianness != LittleEndianFlag)
            {
                throw new ZddFormatException($"Unsupported endianness flag {endianness}; only little-endian (0) files are supported.");
            }

            ReadExact(buffered, intBuffer, "variable count");
            int variableCount = BinaryPrimitives.ReadInt32LittleEndian(intBuffer);
            if (variableCount < 0)
            {
                throw new ZddFormatException($"Variable count must not be negative, but was {variableCount}.");
            }

            ReadExact(buffered, intBuffer, "node count");
            int nodeCount = BinaryPrimitives.ReadInt32LittleEndian(intBuffer);
            int maxNodeCount = NodeTable.MaxCapacity - NodeTable.FirstNodeId;
            if (nodeCount < 0 || nodeCount > maxNodeCount)
            {
                throw new ZddFormatException($"Node count must be between 0 and {maxNodeCount}, but was {nodeCount}.");
            }

            ReadExact(buffered, intBuffer, "root id");
            int rawRootId = BinaryPrimitives.ReadInt32LittleEndian(intBuffer);

            int idLimit = NodeTable.FirstNodeId + nodeCount;
            if (rawRootId != NodeTable.Bottom && rawRootId != NodeTable.Top &&
                (rawRootId < NodeTable.FirstNodeId || rawRootId >= idLimit))
            {
                throw new ZddFormatException($"Root id {rawRootId} is out of range for a file with {nodeCount} node(s).");
            }

            ZddManager manager = new ZddManager(variableCount, EffectiveOptions(options, nodeCount));
            UniqueTable table = manager.Table;

            int[] idMap = nodeCount > 0 ? new int[nodeCount] : Array.Empty<int>();

            for (int i = 0; i < nodeCount; i++)
            {
                int rawId = NodeTable.FirstNodeId + i;

                uint level = VarInt.ReadUInt32(buffered, "level");
                uint rawLo = VarInt.ReadUInt32(buffered, "lo");
                uint rawHi = VarInt.ReadUInt32(buffered, "hi");

                if (level < 1 || level > (uint)variableCount)
                {
                    throw new ZddFormatException($"Node {rawId}: level {level} is out of range 1..{variableCount}.");
                }

                int lo = ResolveReference(rawLo, rawId, idMap, "lo");
                int hi = ResolveReference(rawHi, rawId, idMap, "hi");

                if (hi == NodeTable.Bottom)
                {
                    throw new ZddFormatException(
                        $"Node {rawId}: the 'hi' child must not be the bottom terminal (a real node table never holds one, per the zero-suppression rule).");
                }

                int loLevel = NodeTable.IsTerminal(lo) ? 0 : table.Nodes[lo].Level;
                int hiLevel = NodeTable.IsTerminal(hi) ? 0 : table.Nodes[hi].Level;
                if (level <= (uint)loLevel || level <= (uint)hiLevel)
                {
                    throw new ZddFormatException($"Node {rawId}: level {level} must be strictly greater than its children's levels.");
                }

                idMap[i] = table.GetNode((int)level, lo, hi);
            }

            int rootId = rawRootId == NodeTable.Bottom || rawRootId == NodeTable.Top
                ? rawRootId
                : idMap[rawRootId - NodeTable.FirstNodeId];

            return new Zdd(manager, rootId);
        }

        /// <summary>Translates a raw file-space child reference to the id the fresh manager actually assigned it.</summary>
        /// <exception cref="ZddFormatException"><paramref name="rawRef"/> does not name a terminal or an already-defined earlier node.</exception>
        private static int ResolveReference(uint rawRef, int owningRawId, int[] idMap, string which)
        {
            if (rawRef == NodeTable.Bottom || rawRef == NodeTable.Top)
            {
                return (int)rawRef;
            }

            if (rawRef < NodeTable.FirstNodeId || rawRef >= (uint)owningRawId)
            {
                throw new ZddFormatException(
                    $"Node {owningRawId}: '{which}' child id {rawRef} is out of range (must be a terminal or reference an earlier node in the file).");
            }

            return idMap[rawRef - NodeTable.FirstNodeId];
        }

        private static void ReadExact(Stream stream, Span<byte> buffer, string fieldName)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer.Slice(total));
                if (read == 0)
                {
                    throw new ZddFormatException($"Unexpected end of stream while reading '{fieldName}'.");
                }

                total += read;
            }
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

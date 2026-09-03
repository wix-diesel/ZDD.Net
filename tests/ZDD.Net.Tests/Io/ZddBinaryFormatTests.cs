using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Io;
using ZDD.Net.Specs;
using ZDD.Net.Tests.Harness;
using ZDD.Net.Tests.Stress;

namespace ZDD.Net.Tests.Io
{
    /// <summary>
    /// M5-1 completion criteria: round-tripping a family through <see cref="ZddBinaryFormat"/> preserves
    /// the family exactly (down to node IDs, i.e. canonicity), works for empty/base/singleton/large/deep
    /// ZDDs, and the loaded family still supports the M1 operations. Also covers the "corrupt input never
    /// crashes, always a clear exception" requirement.
    /// </summary>
    public class ZddBinaryFormatTests : IClassFixture<DeepZdd>
    {
        private readonly DeepZdd _deep;

        public ZddBinaryFormatTests(DeepZdd deep) => _deep = deep;

        // ---- Round trips: node IDs must match exactly (canonicity) ----

        [Fact]
        public void RoundTripsEmptyFamilyExactly()
        {
            using ZddManager manager = new ZddManager(3);
            AssertRoundTripsExactly(manager.Empty);
        }

        [Fact]
        public void RoundTripsBaseFamilyExactly()
        {
            using ZddManager manager = new ZddManager(3);
            AssertRoundTripsExactly(manager.Base);
        }

        [Fact]
        public void RoundTripsASingleNodeFamilyExactly()
        {
            using ZddManager manager = new ZddManager(3);
            AssertRoundTripsExactly(manager.Singleton(1));
        }

        [Fact]
        public void RoundTripsAHandBuiltFamilyExactly()
        {
            using ZddManager manager = new ZddManager(3);

            // {{0}, {1, 2}}, built bottom-up so node IDs are deterministic (2, 3, 4).
            Zdd two = manager.CreateNode(2, lo: manager.Empty, hi: manager.Base);
            Zdd oneTwo = manager.CreateNode(1, lo: manager.Empty, hi: two);
            Zdd family = manager.CreateNode(0, lo: oneTwo, hi: manager.Base);

            AssertRoundTripsExactly(family);
        }

        [Fact]
        public void RoundTripsALargePathFamilyExactly()
        {
            Graph grid = Graph.Grid(5, 5);
            using ZddManager manager = new ZddManager(grid.EdgeCount);
            Zdd paths = FrontierBuilder.Build<PathSpec>(manager, new PathSpec(grid, 0, grid.VertexCount - 1));

            Assert.True(paths.Count > 1000);
            AssertRoundTripsExactly(paths);
        }

        [Fact]
        public void RoundTripsADeepZddWithoutStackOverflow()
        {
            AssertRoundTripsExactly(_deep.Full);
        }

        private static void AssertRoundTripsExactly(in Zdd original)
        {
            ZddManager originalManager = original.Manager;
            using MemoryStream stream = new MemoryStream();
            ZddBinaryFormat.Write(original, stream);

            stream.Position = 0;
            Zdd restored = ZddBinaryFormat.Read(stream);

            using ZddManager restoredManager = restored.Manager;

            Assert.Equal(originalManager.VariableCount, restoredManager.VariableCount);
            Assert.Equal(originalManager.NodeCount, restoredManager.NodeCount);

            // The manager was built freshly for just this family (no garbage from unrelated
            // operations), so replaying through the unique table reproduces the same node ID.
            Assert.Equal(original.Id, restored.Id);

            Assert.Equal(original.Count, restored.Count);
        }

        // ---- Loaded families still support M1 operations ----

        [Fact]
        public void LoadedFamilySupportsCountEnumerationAndSample()
        {
            using ZddManager manager = new ZddManager(6);
            Zdd original = ZddFamilies.Build(manager, new[] { 0 }, new[] { 1, 2 }, new[] { 3, 4, 5 }, Array.Empty<int>());

            using MemoryStream stream = new MemoryStream();
            ZddBinaryFormat.Write(original, stream);
            stream.Position = 0;

            Zdd restored = Load(stream);
            using ZddManager restoredManager = restored.Manager;

            Assert.Equal(original.Count, restored.Count);

            int[][] originalSets = original.Sets().OrderBy(s => string.Join(',', s)).ToArray();
            int[][] restoredSets = restored.Sets().OrderBy(s => string.Join(',', s)).ToArray();
            Assert.Equal(originalSets.Length, restoredSets.Length);
            for (int i = 0; i < originalSets.Length; i++)
            {
                Assert.Equal(originalSets[i], restoredSets[i]);
            }

            int[] sample = restored.Sample(new Random(42));
            Assert.True(original.Contains(sample));
        }

        // ---- Malformed input: never crashes, always ZddFormatException ----

        [Fact]
        public void WriteThrowsForDefaultZdd() =>
            Assert.Throws<InvalidOperationException>(() => ZddBinaryFormat.Write(default, new MemoryStream()));

        [Fact]
        public void WriteThrowsForNullStream()
        {
            using ZddManager manager = new ZddManager(1);
            Assert.Throws<ArgumentNullException>(() => ZddBinaryFormat.Write(manager.Empty, null!));
        }

        [Fact]
        public void WriteThrowsForDisposedManager()
        {
            ZddManager manager = new ZddManager(1);
            Zdd family = manager.Empty;
            manager.Dispose();

            Assert.Throws<ObjectDisposedException>(() => ZddBinaryFormat.Write(family, new MemoryStream()));
        }

        [Fact]
        public void ReadThrowsForNullStream() =>
            Assert.Throws<ArgumentNullException>(() => ZddBinaryFormat.Read(null!));

        [Fact]
        public void ReadThrowsOnBadMagicNumber()
        {
            byte[] bytes = BuildValidFile(1, new[] { (Level: 1, Lo: 0, Hi: 1) }, rootId: 2);
            bytes[0] ^= 0xFF;

            AssertThrowsFormatException(bytes);
        }

        [Fact]
        public void ReadThrowsOnUnsupportedFormatVersion()
        {
            byte[] bytes = BuildValidFile(1, new[] { (Level: 1, Lo: 0, Hi: 1) }, rootId: 2, version: 2);

            ZddFormatException ex = AssertThrowsFormatException(bytes);
            Assert.Contains("2", ex.Message);
        }

        [Fact]
        public void ReadThrowsOnUnsupportedEndianness()
        {
            byte[] bytes = BuildValidFile(1, new[] { (Level: 1, Lo: 0, Hi: 1) }, rootId: 2, endianness: 1);

            AssertThrowsFormatException(bytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(10)]
        [InlineData(20)]
        public void ReadThrowsOnTruncatedStream(int truncatedLength)
        {
            byte[] bytes = BuildValidFile(2, new[] { (Level: 1, Lo: 0, Hi: 1), (Level: 2, Lo: 0, Hi: 2) }, rootId: 3);
            byte[] truncated = bytes.Take(Math.Min(truncatedLength, bytes.Length - 1)).ToArray();

            AssertThrowsFormatException(truncated);
        }

        [Fact]
        public void ReadThrowsOnNegativeVariableCount()
        {
            byte[] bytes = BuildRawHeader(variableCount: -1, nodeCount: 0, rootId: 0);
            AssertThrowsFormatException(bytes);
        }

        [Fact]
        public void ReadThrowsOnNegativeNodeCount()
        {
            byte[] bytes = BuildRawHeader(variableCount: 1, nodeCount: -1, rootId: 0);
            AssertThrowsFormatException(bytes);
        }

        [Fact]
        public void ReadThrowsWhenNodeCountExceedsTheMaximum()
        {
            byte[] bytes = BuildRawHeader(variableCount: 1, nodeCount: NodeTable.MaxCapacity - NodeTable.FirstNodeId + 1, rootId: 0);
            AssertThrowsFormatException(bytes);
        }

        [Fact]
        public void ReadThrowsOnOutOfRangeRootId()
        {
            byte[] bytes = BuildValidFile(1, new[] { (Level: 1, Lo: 0, Hi: 1) }, rootId: 999);
            AssertThrowsFormatException(bytes);
        }

        [Fact]
        public void ReadThrowsWhenHiIsTheBottomTerminal()
        {
            // hi == 0 (bottom) can never occur in a real node table: NodeTable.Add rejects it
            // (zero-suppression rule), so a file claiming one is corrupt.
            byte[] bytes = BuildValidFile(1, new[] { (Level: 1, Lo: 0, Hi: 0) }, rootId: 2);
            AssertThrowsFormatException(bytes);
        }

        [Fact]
        public void ReadThrowsOnAForwardReferencingChild()
        {
            // Node 2 (the only node) references node 3, which doesn't exist yet.
            byte[] bytes = BuildValidFile(1, new[] { (Level: 1, Lo: 0, Hi: 3) }, rootId: 2);
            AssertThrowsFormatException(bytes);
        }

        [Fact]
        public void ReadThrowsOnLevelBelowOne()
        {
            byte[] bytes = BuildValidFile(1, new[] { (Level: 0, Lo: 0, Hi: 1) }, rootId: 2);
            AssertThrowsFormatException(bytes);
        }

        [Fact]
        public void ReadThrowsOnLevelAboveVariableCount()
        {
            byte[] bytes = BuildValidFile(1, new[] { (Level: 2, Lo: 0, Hi: 1) }, rootId: 2);
            AssertThrowsFormatException(bytes);
        }

        [Fact]
        public void ReadThrowsWhenLevelDoesNotExceedAChildsLevel()
        {
            // Node 3's hi child is node 2, but both sit at level 1 — not strictly descending.
            byte[] bytes = BuildValidFile(
                1,
                new[] { (Level: 1, Lo: 0, Hi: 1), (Level: 1, Lo: 0, Hi: 2) },
                rootId: 3);

            AssertThrowsFormatException(bytes);
        }

        // ---- Helpers ----

        private static Zdd Load(Stream stream) => ZddBinaryFormat.Read(stream);

        private static ZddFormatException AssertThrowsFormatException(byte[] bytes) =>
            Assert.Throws<ZddFormatException>(() => ZddBinaryFormat.Read(new MemoryStream(bytes)));

        private static byte[] BuildValidFile(
            int variableCount,
            (int Level, int Lo, int Hi)[] nodes,
            int rootId,
            uint? version = null,
            byte? endianness = null)
        {
            using MemoryStream stream = new MemoryStream();
            WriteHeader(stream, version ?? ZddBinaryFormat.FormatVersion, (byte)(endianness ?? 0), variableCount, nodes.Length, rootId);

            foreach ((int level, int lo, int hi) in nodes)
            {
                VarInt.WriteUInt32(stream, (uint)level);
                VarInt.WriteUInt32(stream, (uint)lo);
                VarInt.WriteUInt32(stream, (uint)hi);
            }

            return stream.ToArray();
        }

        private static byte[] BuildRawHeader(int variableCount, int nodeCount, int rootId)
        {
            using MemoryStream stream = new MemoryStream();
            WriteHeader(stream, ZddBinaryFormat.FormatVersion, 0, variableCount, nodeCount, rootId);
            return stream.ToArray();
        }

        private static void WriteHeader(Stream stream, uint version, byte endianness, int variableCount, int nodeCount, int rootId)
        {
            stream.Write("ZDDB"u8);

            Span<byte> buffer = stackalloc byte[4];

            BinaryPrimitives.WriteUInt32LittleEndian(buffer, version);
            stream.Write(buffer);

            stream.WriteByte(endianness);

            BinaryPrimitives.WriteInt32LittleEndian(buffer, variableCount);
            stream.Write(buffer);

            BinaryPrimitives.WriteInt32LittleEndian(buffer, nodeCount);
            stream.Write(buffer);

            BinaryPrimitives.WriteInt32LittleEndian(buffer, rootId);
            stream.Write(buffer);
        }
    }
}

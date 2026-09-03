using System.IO;
using Xunit;
using ZDD.Net.Io;

namespace ZDD.Net.Tests.Io
{
    public class VarIntTests
    {
        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        [InlineData(127u)]
        [InlineData(128u)]
        [InlineData(16383u)]
        [InlineData(16384u)]
        [InlineData(int.MaxValue)]
        [InlineData(uint.MaxValue)]
        public void RoundTripsExactly(uint value)
        {
            using MemoryStream stream = new MemoryStream();
            VarInt.WriteUInt32(stream, value);
            stream.Position = 0;

            Assert.Equal(value, VarInt.ReadUInt32(stream, "value"));
            Assert.Equal(stream.Length, stream.Position);
        }

        [Fact]
        public void SmallValuesEncodeToOneByte()
        {
            using MemoryStream stream = new MemoryStream();
            VarInt.WriteUInt32(stream, 100);

            Assert.Equal(1, stream.Length);
        }

        [Fact]
        public void ReadingFromAnEmptyStreamThrows()
        {
            using MemoryStream stream = new MemoryStream();

            ZddFormatException ex = Assert.Throws<ZddFormatException>(() => VarInt.ReadUInt32(stream, "value"));
            Assert.Contains("value", ex.Message);
        }

        [Fact]
        public void ATruncatedContinuationByteThrows()
        {
            using MemoryStream stream = new MemoryStream(new byte[] { 0x80 });

            Assert.Throws<ZddFormatException>(() => VarInt.ReadUInt32(stream, "value"));
        }

        [Fact]
        public void TooManyContinuationBytesThrows()
        {
            // 6 bytes, all with the continuation bit set: a 32-bit varint needs at most 5.
            using MemoryStream stream = new MemoryStream(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 });

            Assert.Throws<ZddFormatException>(() => VarInt.ReadUInt32(stream, "value"));
        }

        [Fact]
        public void TheMaximumValidFifthByteRoundTrips()
        {
            // uint.MaxValue's last byte carries data 0x0F (bits 28..31) — the largest value whose
            // top 3 bits (which would land at result bits 32..34) are all zero.
            using MemoryStream stream = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F });

            Assert.Equal(uint.MaxValue, VarInt.ReadUInt32(stream, "value"));
        }

        [Theory]
        [InlineData(0x10)]
        [InlineData(0x20)]
        [InlineData(0x40)]
        [InlineData(0x7F)]
        public void AFifthByteWithBitsBeyondTheThirtySecondThrows(byte fifthByte)
        {
            // A well-formed encoder never emits these (they'd shift out of a 32-bit result), so a
            // file containing one is corrupt rather than merely a larger-than-expected value.
            using MemoryStream stream = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, fifthByte });

            ZddFormatException ex = Assert.Throws<ZddFormatException>(() => VarInt.ReadUInt32(stream, "value"));
            Assert.Contains("32 bits", ex.Message);
        }
    }
}

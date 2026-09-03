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
    }
}

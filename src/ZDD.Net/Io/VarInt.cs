using System.IO;

namespace ZDD.Net.Io
{
    /// <summary>
    /// LEB128-style unsigned varint encoding for <see cref="ZddBinaryFormat"/>'s node table: 7 bits of
    /// value per byte, top bit set to mean "more bytes follow". Most node IDs and levels are far
    /// smaller than <c>int.MaxValue</c>, so this shrinks the dominant part of the file substantially
    /// compared to fixed-width 4-byte fields (docs/PLAN.md &#167;9's "consider varint compression").
    /// </summary>
    internal static class VarInt
    {
        /// <summary>A 32-bit value needs at most 5 groups of 7 bits (35 &#8805; 32).</summary>
        private const int MaxBytes = 5;

        /// <summary>Writes <paramref name="value"/> to <paramref name="stream"/> as an unsigned varint.</summary>
        public static void WriteUInt32(Stream stream, uint value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        /// <summary>Reads an unsigned varint from <paramref name="stream"/>.</summary>
        /// <param name="stream">The source stream.</param>
        /// <param name="fieldName">Names the field in exception messages, for a truncated or malformed stream.</param>
        /// <exception cref="ZddFormatException">The stream ended mid-value, or the encoding uses more than 5 continuation bytes.</exception>
        public static uint ReadUInt32(Stream stream, string fieldName)
        {
            uint result = 0;
            int shift = 0;

            for (int i = 0; ; i++)
            {
                int b = stream.ReadByte();
                if (b < 0)
                {
                    throw new ZddFormatException($"Unexpected end of stream while reading '{fieldName}' (a varint).");
                }

                if (i >= MaxBytes)
                {
                    throw new ZddFormatException($"Malformed varint while reading '{fieldName}': too many continuation bytes.");
                }

                result |= (uint)(b & 0x7F) << shift;

                if ((b & 0x80) == 0)
                {
                    return result;
                }

                shift += 7;
            }
        }
    }
}

using System;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Thrown when a <see cref="ZddBinaryFormat"/> stream cannot be read &#8212; a truncated stream, a
    /// bad magic number or endianness flag, an unsupported format version, or a node whose fields
    /// violate the reduction rules (out-of-range reference, non-ascending level, <c>hi == bottom</c>).
    /// </summary>
    /// <remarks>
    /// Kept distinct from <see cref="Io.GraphFormatException"/> (a different domain, no line numbers
    /// here since the format is binary) so callers can catch the two independently.
    /// </remarks>
    public sealed class ZddFormatException : FormatException
    {
        /// <summary>Creates an exception describing why a binary ZDD stream could not be read.</summary>
        /// <param name="message">A description of what went wrong.</param>
        public ZddFormatException(string message)
            : base(message)
        {
        }
    }
}

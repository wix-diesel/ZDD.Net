using System;

namespace ZDD.Net.Io
{
    /// <summary>
    /// Thrown when a graph text format (DIMACS, edge list, or the library's simple text format) cannot
    /// be parsed &#8212; a malformed line, an out-of-range vertex, or a header whose declared edge count
    /// does not match the edges actually present. Always names the offending 1-based line number, so a
    /// caller reading a multi-thousand-edge file can find the problem without a binary search.
    /// </summary>
    public sealed class GraphFormatException : FormatException
    {
        /// <summary>The 1-based line number of the input where the error was found.</summary>
        public int LineNumber { get; }

        /// <summary>Creates an exception reporting a parse failure at <paramref name="lineNumber"/>.</summary>
        /// <param name="lineNumber">The 1-based line number where the error was found.</param>
        /// <param name="message">A description of what went wrong on that line.</param>
        public GraphFormatException(int lineNumber, string message)
            : base($"Line {lineNumber}: {message}")
        {
            LineNumber = lineNumber;
        }
    }
}

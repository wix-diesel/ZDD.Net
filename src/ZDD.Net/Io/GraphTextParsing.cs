using System;
using System.Globalization;

namespace ZDD.Net.Io
{
    /// <summary>Small parsing helpers shared by the graph text-format readers in this namespace.</summary>
    internal static class GraphTextParsing
    {
        private static readonly char[] TokenSeparators = { ' ', '\t', ',' };

        /// <summary>Splits a line on spaces, tabs, and commas, discarding empty tokens.</summary>
        public static string[] SplitTokens(string line) =>
            line.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>
        /// Parses <paramref name="token"/> as an integer, throwing <see cref="GraphFormatException"/>
        /// naming <paramref name="lineNumber"/> and <paramref name="field"/> on failure.
        /// </summary>
        public static int ParseInt(string token, int lineNumber, string field)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                throw new GraphFormatException(lineNumber, $"Expected an integer for {field}, but found '{token}'.");
            }

            return value;
        }
    }
}

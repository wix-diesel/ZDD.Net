using System.IO;

namespace ZDD.Net.Samples.Cli
{
    /// <summary>Small pieces shared by every subcommand's argument parsing and reporting.</summary>
    internal static class CliOutput
    {
        /// <summary>Writes one <c>name: value</c> report line, padding the name like the original CLI did.</summary>
        public static void WriteField(TextWriter output, string name, string value)
        {
            output.Write(name.PadRight(12));
            output.Write(": ");
            output.WriteLine(value);
        }

        /// <summary>
        /// Consumes the argument after <paramref name="option"/> as its value, advancing
        /// <paramref name="index"/> past it.
        /// </summary>
        /// <returns><see langword="false"/>, with <paramref name="error"/> set, if there is no next argument.</returns>
        public static bool TryTakeValue(
            string[] args,
            ref int index,
            string option,
            out string value,
            out string? error)
        {
            if (index + 1 >= args.Length)
            {
                value = string.Empty;
                error = $"{option} needs a value.";
                return false;
            }

            value = args[++index];
            error = null;
            return true;
        }
    }
}

using System.Text;

namespace Updaemon.Services
{
    /// <summary>
    /// Escaping for values written into systemd unit files, and reading quoted values back.
    /// </summary>
    public static class SystemdSyntax
    {
        /// <summary>systemd expands <c>%x</c> specifiers in most settings; <c>%%</c> is a literal percent sign.</summary>
        public static string EscapeSpecifiers(string value) => value.Replace("%", "%%");

        /// <summary>
        /// Quotes one ExecStart word: double quotes with backslash escapes, <c>%%</c> for specifiers
        /// and <c>$$</c> so systemd does not substitute environment variables.
        /// </summary>
        public static string QuoteArgument(string value)
        {
            string escaped = value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("%", "%%")
                .Replace("$", "$$");
            return $"\"{escaped}\"";
        }

        /// <summary>
        /// Splits a value such as an <c>Environment=</c> line's into its words: separated by whitespace,
        /// each optionally in double quotes with <c>\"</c> and <c>\\</c> escapes. Null for anything
        /// only systemd itself could read: specifiers (<c>%</c>), single quotes or other escapes.
        /// </summary>
        public static IReadOnlyList<string>? SplitWords(string value)
        {
            List<string> words = new List<string>();
            int i = 0;
            while (i < value.Length)
            {
                if (char.IsWhiteSpace(value[i]))
                {
                    i++;
                    continue;
                }

                StringBuilder word = new StringBuilder();
                bool quoted = value[i] == '"';
                i += quoted ? 1 : 0;
                while (i < value.Length && (quoted ? value[i] != '"' : !char.IsWhiteSpace(value[i])))
                {
                    char c = value[i++];
                    if (c == '%' || (!quoted && (c == '"' || c == '\'' || c == '\\')))
                    {
                        return null;
                    }

                    if (c == '\\')
                    {
                        if (i >= value.Length || (value[i] != '"' && value[i] != '\\'))
                        {
                            return null;
                        }

                        c = value[i++];
                    }

                    word.Append(c);
                }

                if (quoted)
                {
                    if (i >= value.Length)
                    {
                        return null;
                    }

                    i++;
                    if (i < value.Length && !char.IsWhiteSpace(value[i]))
                    {
                        return null;
                    }
                }

                words.Add(word.ToString());
            }

            return words;
        }
    }
}

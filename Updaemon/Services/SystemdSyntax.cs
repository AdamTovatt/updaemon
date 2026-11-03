namespace Updaemon.Services
{
    /// <summary>
    /// Escaping for values written into systemd unit files.
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
    }
}

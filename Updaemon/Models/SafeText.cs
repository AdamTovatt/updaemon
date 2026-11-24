namespace Updaemon.Models
{
    /// <summary>
    /// The one rule for text that ends up in a generated file (env file, unit, plist, wrapper) or comes from
    /// elsewhere into a message: no control characters. In a file, a line break would start a line of its
    /// own in any of those formats, for example a second <c>User=</c> in a systemd unit.
    /// </summary>
    public static class SafeText
    {
        public static bool IsSafe(string value) => !value.Any(char.IsControl);

        /// <summary>
        /// Text from elsewhere, to end a sentence with in a message or a record: its lines on one, joined by
        /// <c> / </c>, without control characters or a final period, cut off after <paramref name="maxLength"/>.
        /// </summary>
        public static string ToLine(string text, int maxLength)
        {
            IEnumerable<string> lines = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => string.Concat(line.Where(character => !char.IsControl(character))))
                .Where(line => line.Length > 0);
            string joined = string.Join(" / ", lines).TrimEnd('.');
            return joined.Length <= maxLength ? joined : joined[..maxLength] + "...";
        }
    }
}

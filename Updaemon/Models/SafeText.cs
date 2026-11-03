namespace Updaemon.Models
{
    /// <summary>
    /// The one rule for text that ends up in a generated file (env file, unit, plist, wrapper): no
    /// control characters. A line break would start a line of its own in any of those formats, for
    /// example a second <c>User=</c> in a systemd unit.
    /// </summary>
    public static class SafeText
    {
        public static bool IsSafe(string value) => !value.Any(char.IsControl);
    }
}

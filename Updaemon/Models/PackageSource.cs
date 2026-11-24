namespace Updaemon.Models
{
    /// <summary>Where a package's releases come from: <c>&lt;plugin&gt;:&lt;remote&gt;</c>, for example <c>github:owner/repo</c>.</summary>
    public sealed record PackageSource(string Plugin, string Remote)
    {
        /// <summary>Reads <c>&lt;plugin&gt;:&lt;remote&gt;</c>; null when <paramref name="text"/> is not that.</summary>
        public static PackageSource? TryParse(string text)
        {
            int separator = text.IndexOf(':');
            return separator <= 0 || separator == text.Length - 1 || !SafeText.IsSafe(text)
                ? null
                : new PackageSource(text.Substring(0, separator), text.Substring(separator + 1));
        }

        public override string ToString() => $"{Plugin}:{Remote}";
    }
}

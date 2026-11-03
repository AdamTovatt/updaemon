namespace Updaemon.Models
{
    /// <summary>
    /// A plugin build to fetch: its address, the file name it is saved under, and the SHA256SUMS it must
    /// match (null only for a plain URL, which is not verified).
    /// </summary>
    /// <param name="GitHub">
    /// Set for a github: source: both requests then go through <see cref="Common.Utilities.GitHubAccess"/>,
    /// with its token if one is set, and a refusal for want of one says so. Null for any other source,
    /// whose addresses are fetched as they are.
    /// </param>
    public sealed record PluginDownload(Uri Build, string FileName, Uri? Checksums, GitHubFetch? GitHub)
    {
        public override string ToString() => Build.ToString();
    }

    /// <summary>A fetch from GitHub, with the token if one is set.</summary>
    public sealed record GitHubFetch(string? Token)
    {
        /// <summary>Never the token: it must not reach output.</summary>
        public override string ToString() => Token == null ? "GitHub without a token" : "GitHub with a token";
    }
}

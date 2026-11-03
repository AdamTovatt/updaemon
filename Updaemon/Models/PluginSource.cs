using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Updaemon.Configuration;

namespace Updaemon.Models
{
    /// <summary>
    /// Where a plugin is installed from: updaemon's plugin registry, by name, a GitHub repository's
    /// releases, or a URL. A registry plugin comes from updaemon's own release, so it follows updaemon;
    /// a GitHub plugin follows its repository's latest release; a URL plugin is only ever replaced by hand.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(Registry), "registry")]
    [JsonDerivedType(typeof(GitHub), "github")]
    [JsonDerivedType(typeof(Url), "url")]
    public abstract partial record PluginSource
    {
        /// <summary>How <c>dist-install</c> takes a source, for its usage and for messages that point to it.</summary>
        public const string InstallUsage = "updaemon dist-install [--as <alias>] <plugin-name|github:owner/repo|url>";

        private const string GitHubPrefix = "github:";

        // Private so the kinds below are the only ones.
        private PluginSource()
        {
        }

        /// <summary>Whether its download is checked against its release's SHA256SUMS; a plain URL has none.</summary>
        [JsonIgnore]
        public bool HasPublishedChecksums => this is not Url;

        /// <summary>
        /// Whether this is a registry plugin built for another updaemon than <paramref name="running"/>.
        /// Such a plugin is not run: it is reinstalled from the running updaemon's release first (see
        /// <see cref="Services.PluginProvider"/>).
        /// </summary>
        public bool IsOutOfLineWith(UpdaemonVersion running) => this is Registry registry && registry.Release != running;

        /// <summary>
        /// Reads what <c>dist-install</c> was given: an http(s) URL, <c>github:&lt;owner&gt;/&lt;repo&gt;</c>
        /// at the tag <paramref name="latestTagOf"/> gives for it, or else a registry name, from the
        /// release of the running updaemon, <paramref name="running"/>.
        /// </summary>
        /// <exception cref="FormatException">A <c>github:</c> source is not an owner and a repository name.</exception>
        public static async Task<PluginSource> ParseAsync(string text, UpdaemonVersion running, Func<string, string, Task<string>> latestTagOf)
        {
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return new Url(text);
            }

            if (!text.StartsWith(GitHubPrefix, StringComparison.Ordinal))
            {
                return new Registry(text, running);
            }

            if (text[GitHubPrefix.Length..].Split('/') is not [string owner, string repo] || !IsGitHubName(owner) || !IsGitHubName(repo))
            {
                throw new FormatException($"'{text}' is not {GitHubPrefix}<owner>/<repo>, each of letters, digits, '.', '_' and '-'.");
            }

            return new GitHub(owner, repo, await latestTagOf(owner, repo));
        }

        private static bool IsGitHubName(string name) => GitHubNameRegex().IsMatch(name) && name is not "." and not "..";

        [GeneratedRegex("^[A-Za-z0-9._-]+$")]
        private static partial Regex GitHubNameRegex();

        /// <summary>A first-party plugin named in the registry, built in updaemon's release <paramref name="Release"/>.</summary>
        public sealed record Registry(string Name, [property: JsonRequired] UpdaemonVersion Release) : PluginSource
        {
            public override string ToString() => Name;
        }

        /// <summary>
        /// A third-party plugin: the <c>*-&lt;rid&gt;</c> asset of release <paramref name="Tag"/> of
        /// <paramref name="Owner"/>/<paramref name="Repo"/> on GitHub.
        /// </summary>
        public sealed record GitHub(string Owner, string Repo, [property: JsonRequired] string Tag) : PluginSource
        {
            /// <summary>
            /// A later release that was refused for good (its checksum, its build for this machine, or its
            /// plugin protocol), which update passes over while it is the latest. Installing the plugin
            /// again with <c>dist-install</c> clears it.
            /// </summary>
            public string? SkippedTag { get; init; }

            public override string ToString() => $"{GitHubPrefix}{Owner}/{Repo}";
        }

        /// <summary>
        /// A plugin binary downloaded from an address the user gave.
        /// </summary>
        public sealed record Url(string Address) : PluginSource
        {
            public override string ToString() => Address;
        }
    }
}

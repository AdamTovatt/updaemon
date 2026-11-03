using Updaemon.Configuration;
using Updaemon.Interfaces;

namespace Updaemon.Tests.Mocks
{
    /// <summary>
    /// Updaemon's releases as shell scripts: each build prints what <c>--version</c> should, unless told otherwise.
    /// </summary>
    public class MockUpdaemonReleases : IUpdaemonReleases
    {
        private readonly Dictionary<UpdaemonVersion, string> _builds = new Dictionary<UpdaemonVersion, string>();

        /// <summary>The latest release; null as when the release page names no x.y.z version.</summary>
        public UpdaemonVersion? Latest { get; set; }

        public List<UpdaemonVersion> Downloads { get; } = new List<UpdaemonVersion>();

        /// <summary>Thrown when reading the latest release, as when GitHub cannot be reached.</summary>
        public HttpRequestException? LatestFailure { get; set; }

        /// <summary>Thrown when downloading, as when the release has no build for this machine or it fails its checksum.</summary>
        public Exception? DownloadFailure { get; set; }

        /// <summary>Publishes <paramref name="version"/> as the latest release; its build prints <paramref name="printedVersion"/>, by default its own.</summary>
        public void Publish(UpdaemonVersion version, string? printedVersion = null, int exitCode = 0)
        {
            PublishBuild(version, $"#!/bin/sh\necho '{printedVersion ?? version.ToString()}'\nexit {exitCode}\n");
        }

        /// <summary>Publishes <paramref name="version"/> as the latest release, its build being <paramref name="content"/>.</summary>
        public void PublishBuild(UpdaemonVersion version, string content)
        {
            _builds[version] = content;
            Latest = version;
        }

        public Task<UpdaemonVersion?> GetLatestVersionAsync(CancellationToken cancellationToken = default)
        {
            if (LatestFailure != null)
            {
                throw LatestFailure;
            }

            return Task.FromResult(Latest);
        }

        public Task DownloadAsync(UpdaemonVersion release, string destinationPath, CancellationToken cancellationToken = default)
        {
            if (DownloadFailure != null)
            {
                throw DownloadFailure;
            }

            Downloads.Add(release);
            File.WriteAllText(destinationPath, _builds[release]);
            return Task.CompletedTask;
        }
    }
}

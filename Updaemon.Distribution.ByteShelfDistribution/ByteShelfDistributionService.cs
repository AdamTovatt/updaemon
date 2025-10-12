using ByteShelfClient;
using ByteShelfCommon;
using Updaemon.Common;
using Updaemon.Common.Utilities;
using Updaemon.Distribution.ByteShelfDistribution.Interfaces;

namespace Updaemon.Distribution.ByteShelfDistribution
{
    /// <summary>
    /// Distribution service that retrieves versioned releases from ByteShelf storage.
    /// Uses a hierarchical structure: tenant → app subtenant → version subtenant → files
    /// </summary>
    public class ByteShelfDistributionService : IDistributionService
    {
        private readonly IVersionParser _versionParser;
        private readonly IDownloadPostProcessor _postProcessor;
        private IByteShelfApiClient? _apiClient;

        public ByteShelfDistributionService(
            IVersionParser versionParser,
            IDownloadPostProcessor postProcessor)
        {
            _versionParser = versionParser;
            _postProcessor = postProcessor;
        }

        public Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default)
        {
            string? apiKey = secrets.GetValueIgnoreCase("byteShelfApiKey");
            string? url = secrets.GetValueIgnoreCase("byteShelfUrl");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("ByteShelf API key is required. Set it using: updaemon secret-set byteShelfApiKey <your-key>");
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException("ByteShelf URL is required. Set it using: updaemon secret-set byteShelfUrl <your-url>");
            }

            HttpClient httpClient = HttpShelfFileProvider.CreateHttpClient(url);
            IShelfFileProvider fileProvider = new HttpShelfFileProvider(httpClient, apiKey);
            _apiClient = new Services.ByteShelfApiClient(fileProvider, _versionParser);

            return Task.CompletedTask;
        }

        public async Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            if (_apiClient == null)
            {
                throw new InvalidOperationException("API client not initialized. Call InitializeAsync first.");
            }

            (string appSubtenantName, string? _) = ParseServiceName(serviceName);

            // Find the app subtenant by display name
            (string id, TenantInfoResponse info)? appSubtenant = await _apiClient.GetSubTenantByDisplayNameAsync(appSubtenantName, cancellationToken);
            if (appSubtenant == null)
            {
                Console.WriteLine($"Warning: App subtenant '{appSubtenantName}' not found in ByteShelf");
                return null;
            }

            // Get all version subtenants under the app subtenant
            Dictionary<string, TenantInfoResponse> versionSubtenants = await _apiClient.GetVersionSubTenantsAsync(appSubtenant.Value.id, cancellationToken);

            if (versionSubtenants.Count == 0)
            {
                Console.WriteLine($"Warning: No version subtenants found under '{appSubtenantName}'");
                return null;
            }

            // Parse each subtenant name as a version
            Version? latestVersion = null;

            foreach (KeyValuePair<string, TenantInfoResponse> kvp in versionSubtenants)
            {
                Version? parsedVersion = _versionParser.Parse(kvp.Value.DisplayName);

                if (parsedVersion == null)
                {
                    Console.WriteLine($"Warning: Skipping subtenant '{kvp.Value.DisplayName}' - not a valid version");
                    continue;
                }

                if (latestVersion == null || parsedVersion > latestVersion)
                {
                    latestVersion = parsedVersion;
                }
            }

            return latestVersion;
        }

        public async Task DownloadVersionAsync(
            string serviceName,
            Version version,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            if (_apiClient == null)
            {
                throw new InvalidOperationException("API client not initialized. Call InitializeAsync first.");
            }

            (string appSubtenantName, string? filenamePattern) = ParseServiceName(serviceName);

            // Find the app subtenant by display name
            (string id, TenantInfoResponse info)? appSubtenant = await _apiClient.GetSubTenantByDisplayNameAsync(appSubtenantName, cancellationToken);
            if (appSubtenant == null)
            {
                throw new InvalidOperationException($"App subtenant '{appSubtenantName}' not found in ByteShelf");
            }

            // Find the version subtenant
            (string id, TenantInfoResponse info)? versionSubtenant = await _apiClient.GetVersionSubTenantByVersionAsync(appSubtenant.Value.id, version, cancellationToken);
            if (versionSubtenant == null)
            {
                throw new InvalidOperationException($"Version '{version}' not found under '{appSubtenantName}'");
            }

            // Get all files in the version subtenant
            IEnumerable<ShelfFileMetadata> files = await _apiClient.GetFilesInSubTenantAsync(versionSubtenant.Value.id, cancellationToken);
            ShelfFileMetadata[] filesArray = files.ToArray();

            // Find the matching file based on pattern
            ShelfFileMetadata matchedFile = FindMatchingFile(filesArray, filenamePattern, appSubtenantName, versionSubtenant.Value.info.DisplayName);

            // Ensure target directory exists
            if (!Directory.Exists(targetPath))
            {
                Directory.CreateDirectory(targetPath);
            }

            // Download the file
            string targetFilePath = Path.Combine(targetPath, matchedFile.OriginalFilename);
            await _apiClient.DownloadFileAsync(versionSubtenant.Value.id, matchedFile.Id, targetFilePath, cancellationToken);

            // Post-process (extract if zip)
            await _postProcessor.ProcessAsync(targetPath, cancellationToken);
        }

        /// <summary>
        /// Parses a service name into app subtenant name and optional filename pattern.
        /// </summary>
        /// <param name="serviceName">Service name in format "AppName" or "AppName/pattern"</param>
        /// <returns>Tuple of (appSubtenantName, filenamePattern)</returns>
        private (string appSubtenantName, string? filenamePattern) ParseServiceName(string serviceName)
        {
            // Strip leading and trailing slashes
            string normalized = serviceName.Trim('/');

            string[] parts = normalized.Split('/');

            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                throw new ArgumentException($"Invalid service name format: '{serviceName}'. Expected 'AppName' or 'AppName/pattern'");
            }

            string appSubtenantName = parts[0];
            string? filenamePattern = parts.Length > 1 ? parts[1] : null;

            return (appSubtenantName, filenamePattern);
        }

        /// <summary>
        /// Finds a matching file from a list based on a filename pattern.
        /// Supports wildcards like *.zip, linux-*.zip, etc.
        /// </summary>
        /// <param name="files">Array of available files</param>
        /// <param name="pattern">Filename pattern (null means single file required)</param>
        /// <param name="appName">App subtenant name (for error messages)</param>
        /// <param name="versionName">Version subtenant name (for error messages)</param>
        /// <returns>The matching file</returns>
        private ShelfFileMetadata FindMatchingFile(ShelfFileMetadata[] files, string? pattern, string appName, string versionName)
        {
            if (pattern == null)
            {
                // No pattern specified - require exactly one file
                if (files.Length == 0)
                {
                    throw new InvalidOperationException($"No files found in version '{versionName}' for '{appName}'");
                }

                if (files.Length > 1)
                {
                    string fileNames = string.Join(", ", files.Select(f => f.OriginalFilename));
                    throw new InvalidOperationException(
                        $"Multiple files found in version '{versionName}' for '{appName}': {fileNames}. " +
                        $"Please specify a pattern like '{appName}/*.zip'");
                }

                return files[0];
            }

            // Convert wildcard pattern to regex
            string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(
                regexPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            ShelfFileMetadata[] matchingFiles = files.Where(f => regex.IsMatch(f.OriginalFilename)).ToArray();

            if (matchingFiles.Length == 0)
            {
                string fileNames = string.Join(", ", files.Select(f => f.OriginalFilename));
                throw new InvalidOperationException(
                    $"No files matching pattern '{pattern}' found in version '{versionName}' for '{appName}'. " +
                    $"Available files: {fileNames}");
            }

            if (matchingFiles.Length > 1)
            {
                string matchingNames = string.Join(", ", matchingFiles.Select(f => f.OriginalFilename));
                throw new InvalidOperationException(
                    $"Multiple files match pattern '{pattern}' in version '{versionName}' for '{appName}': {matchingNames}. " +
                    $"Please use a more specific pattern.");
            }

            return matchingFiles[0];
        }
    }
}


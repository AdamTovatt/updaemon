# Updaemon.GithubDistributionService

A distribution plugin for Updaemon that fetches versioned releases from GitHub repositories.

## Overview

This plugin enables Updaemon to download and manage applications distributed via GitHub Releases. It automatically detects the latest version from release tags, supports asset pattern matching for repositories with multiple release files, and handles authentication for private repositories and higher rate limits.

## Features

- **Automatic Version Detection** - Parses version information from GitHub release tags (supports `v1.2.3`, `1.2.3`, `curl-8_16_0`, and other formats)
- **Asset Pattern Matching** - Use wildcards to select specific assets from releases with multiple files
- **Optional Authentication** - GitHub token support for private repositories and higher rate limits
- **Automatic Extraction** - Automatically extracts and unwraps `.zip` archives (only `.zip` format is supported)
- **AOT Compiled** - Fast startup with no runtime dependencies
- **Rate Limit Friendly** - Efficient API usage with clear error messages

## Installation

### Building from Source

```bash
# Navigate to the project directory
cd Updaemon.GithubDistributionService

# Publish as AOT-compiled executable for Linux
dotnet publish -c Release

# The executable will be in: bin/Release/net8.0/linux-x64/publish/
```

### Installing the Plugin

```bash
# Install directly from a URL (if hosted)
sudo updaemon dist-install https://example.com/updaemon-github-plugin

# Or copy the built executable manually
sudo cp bin/Release/net8.0/linux-x64/publish/Updaemon.GithubDistributionService /var/lib/updaemon/plugins/github-dist
sudo chmod +x /var/lib/updaemon/plugins/github-dist

# Update updaemon config to use this plugin
# Edit /var/lib/updaemon/config.json and set:
# "distributionPluginPath": "/var/lib/updaemon/plugins/github-dist"
```

## Configuration

### Setting up GitHub Token (Optional but Recommended)

GitHub tokens are optional but highly recommended to avoid rate limits and access private repositories.

```bash
# Create a GitHub Personal Access Token at:
# https://github.com/settings/tokens

# Set the token in updaemon
sudo updaemon secret-set githubToken ghp_your_token_here
```

**Token Permissions Required:**
- For public repositories: No specific permissions needed
- For private repositories: `repo` scope

**Rate Limits:**
- Without token: 60 requests per hour
- With token: 5,000 requests per hour

### Service Name Format

The service name determines which GitHub repository and asset to download:

**Format: `owner/repo/pattern`**
- Pattern supports wildcards: `*` (matches anything) and `?` (matches one character)
- Example: `microsoft/winget-cli/*.zip`
- If the release has exactly one asset, you can omit the pattern and use just `owner/repo`

## Usage

### Example: Pattern Matching with Authentication

```bash
# Set up GitHub token (optional but recommended for rate limits)
sudo updaemon secret-set githubToken ghp_your_token_here

# Create a new service
sudo updaemon new myapp

# Set the remote with a pattern to match the desired asset
sudo updaemon set-remote myapp organization/myapp/myapp-linux-*.zip

# Update to the latest version
sudo updaemon update myapp
```

## Service Name Patterns

### Pattern Examples

| Pattern | Matches | Use Case |
|---------|---------|----------|
| `owner/repo` | Single asset only | Simple releases with one file |
| `owner/repo/*.zip` | Any `.zip` file | Releases with mixed file types |
| `owner/repo/app-linux-*.zip` | Files starting with `app-linux-` and ending with `.zip` | Platform-specific binaries |
| `owner/repo/*-x64.zip` | Files ending with `-x64.zip` | Architecture-specific archives |
| `owner/repo/app-?.?.?.zip` | `app-1.2.3.zip`, `app-2.0.1.zip` | Specific filename structures |

### Pattern Matching Behavior

- Patterns are **case-insensitive**
- If no pattern matches, you'll get a clear error listing all available assets
- If multiple assets match, you'll be prompted to use a more specific pattern
- Leading and trailing slashes in service names are automatically stripped

## Version Parsing

The plugin automatically extracts version numbers from GitHub release tag names.

### Supported Formats

| Tag Name | Parsed Version | Notes |
|----------|----------------|-------|
| `v1.2.3` | `1.2.3` | Standard semantic versioning with `v` prefix |
| `1.2.3` | `1.2.3` | Standard semantic versioning without prefix |
| `v1.2.3-beta` | `1.2.3` | Pre-release suffixes are stripped |
| `curl-8_16_0` | `8.16.0` | Underscores converted to dots |
| `release-1.0.0` | `1.0.0` | Common prefixes stripped |

### Version Parsing Behavior

- The parser tries to extract version numbers from any format
- Non-numeric prefixes and suffixes are automatically stripped
- Underscores are treated as version delimiters
- If parsing fails completely, the service is skipped with a warning

## Architecture

This plugin implements the `IDistributionService` interface from **Updaemon.Common**:

```csharp
public interface IDistributionService
{
    Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default);
    Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default);
    Task DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default);
}
```

**Components:**
- `GithubDistributionService` - Main service implementation
- `GithubApiClient` - GitHub API communication layer
- `VersionParser` - Extracts versions from tag names
- `DownloadPostProcessor` - Handles zip extraction and unwrapping

For more details on creating distribution plugins, see [Updaemon.Common/README.md](../Updaemon.Common/README.md).

## Troubleshooting

### Rate Limit Errors

**Problem:** `API rate limit exceeded`

**Solution:** Set up a GitHub token as described in [Configuration](#configuration).

### No Matching Assets

**Problem:** `No assets matching pattern 'xyz' found`

**Solution:** 
1. Visit the GitHub release page manually
2. Check what assets are available
3. Adjust your pattern to match the actual filenames

### Version Not Detected

**Problem:** Service update says "No version detected"

**Solution:** Check that the repository has at least one release. GitHub tags without releases are not detected.

### Multiple Assets Match

**Problem:** `Multiple assets match pattern`

**Solution:** Use a more specific pattern. For example, if `*.zip` matches both `app-windows.zip` and `app-linux.zip`, use `*-linux.zip` instead.

## Example: Real-World Usage

Here's a complete example deploying an application via GitHub releases:

```bash
# 1. Install and configure the plugin (one-time setup)
sudo updaemon dist-install https://example.com/updaemon-github-plugin
sudo updaemon secret-set githubToken ghp_your_token_here

# 2. Create a service for your application
sudo updaemon new myapp

# 3. Point it to the GitHub repository with a pattern
sudo updaemon set-remote myapp your-org/myapp/myapp-linux-*.zip

# 4. Download and install the latest version
sudo updaemon update myapp

# 5. The service is now running and will auto-update when new versions are released
systemctl status myapp
```

## Contributing

This plugin is part of the Updaemon project. For contribution guidelines, see the main repository.

## License

[Same as Updaemon]


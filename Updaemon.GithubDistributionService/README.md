# Updaemon.GithubDistributionService

A distribution plugin for Updaemon that fetches versioned releases from GitHub repositories.

## Overview

This plugin enables Updaemon to download and manage applications distributed via GitHub Releases. It automatically detects the latest version from release tags, supports asset pattern matching for repositories with multiple release files, and handles authentication for private repositories and higher rate limits.

## Features

- **Automatic Version Detection** - Parses version information from GitHub release tags (supports `v1.2.3`, `1.2.3`, `curl-8_16_0`, and other formats)
- **Asset Pattern Matching** - Use wildcards to select specific assets from releases with multiple files
- **Optional Authentication** - GitHub token support for private repositories and higher rate limits
- **Checksums** - When the release has a `SHA256SUMS` asset, the download is checked against it before it is extracted, and refused on a mismatch or a missing line
- **Automatic Extraction** - Extracts and unwraps `.zip` and `.tar.gz` archives, keeping file modes; an archive that cannot be extracted fails the download
- **AOT Compiled** - Fast startup with no runtime dependencies
- **Rate Limit Friendly** - Efficient API usage with clear error messages

## Installation

```bash
updaemon dist-install github
```

This installs the plugin from the release of the updaemon you run, built for your machine. The examples here run in your user scope; run them with `sudo` for the system scope.

## Configuration

### Setting up GitHub Token (Optional but Recommended)

GitHub tokens are optional but highly recommended to avoid rate limits and access private repositories.

Create a GitHub Personal Access Token at:
https://github.com/settings/tokens

```bash
updaemon secret-set github githubToken
```

It asks for the token without showing it, which keeps it out of your shell's history. You can also skip this step: when GitHub refuses a request for want of a token, `install`, `update` and `dist-install` ask for it at a terminal, save it and try again. Under the update timer, there is no one to ask, so the update fails in one line naming the command above.

**Token Permissions Required:**
- For public repositories: No specific permissions needed
- For private repositories: `repo` scope

**Rate Limits:**
- Without token: 60 requests per hour
- With token: 5,000 requests per hour

### Service Name Format

The service name determines which GitHub repository and asset to download:

**Format: `owner/repo` or `owner/repo/<pattern>`**
- With just `owner/repo`, the plugin picks the build for the machine updaemon runs on: the one asset named `*-<rid>.zip`, `*-<rid>.tar.gz` or `*-<rid>` (a bare binary), where `<rid>` is the machine's runtime identifier such as `linux-arm64`, `linux-x64` or `osx-arm64`. Name your release assets this way, e.g. `myapp-linux-arm64.zip`.
- A pattern picks the asset instead. It supports wildcards: `*` (matches anything) and `?` (matches one character). Example: `microsoft/winget-cli/*.zip`

## Usage

### Example: Pattern Matching with Authentication

```bash
# Set up GitHub token (optional but recommended for rate limits); it is asked for without echo
updaemon secret-set github githubToken

# Install the app, picking its asset with a pattern
updaemon install myapp --from github:organization/myapp/myapp-*-self-contained.zip

# Update to the latest version
updaemon update myapp
```

Without a pattern (`--from github:organization/myapp`), the plugin picks the asset built for this machine.

## Service Name Patterns

### Pattern Examples

| Pattern | Matches | Use Case |
|---------|---------|----------|
| `owner/repo` | `*-<rid>.zip`, `*-<rid>.tar.gz` or `*-<rid>` for this machine | Releases that follow the naming convention |
| `owner/repo/*.zip` | Any `.zip` file | Releases with mixed file types |
| `owner/repo/app-linux-*.zip` | Files starting with `app-linux-` and ending with `.zip` | Platform-specific binaries |
| `owner/repo/*-x64.zip` | Files ending with `-x64.zip` | Architecture-specific archives |
| `owner/repo/app-?.?.?.zip` | `app-1.2.3.zip`, `app-2.0.1.zip` | Specific filename structures |

### Pattern Matching Behavior

- Patterns and the runtime suffix are **case-insensitive**
- If no asset matches, or several do, the download fails with the release's asset names, one per line, and the `owner/repo/<pattern>` form to pick one
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
    DistributionServiceInformation GetServiceInformation();
    Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default);
    Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default);
    Task<DownloadResult> DownloadVersionAsync(string serviceName, Version version, string runtimeIdentifier, string targetPath, CancellationToken cancellationToken = default);
}
```

**Components:**
- `GithubDistributionService` - Main service implementation; checks the chosen asset against the release's `SHA256SUMS` (via `Sha256Sums`) before extracting it
- `GithubApiClient` - GitHub API communication layer
- `VersionParser` - Extracts versions from tag names
- `DownloadPostProcessor` - Extracts a `.zip` or `.tar.gz` and unwraps a single top-level directory

For more details on creating distribution plugins, see [Updaemon.Common/README.md](../Updaemon.Common/README.md).

## Troubleshooting

### Rate Limit Errors

**Problem:** `GitHub's rate limit is used up until 14:05 UTC.`

**Solution:** Wait until then, or set up a GitHub token as described in [Configuration](#configuration); updaemon says so when none is set.

### Repository Not Found

**Problem:** `owner/repo was not found on GitHub: it does not exist, or it is private and needs a githubToken.`

**Solution:** Check the name after `github:`. If the repository is private, give a token with the `repo` scope; at a terminal, updaemon asks for it.

**Problem:** `owner/repo was not found on GitHub: it does not exist, or the githubToken cannot read it.`

**Solution:** Check the name, and that the token has access to the repository.

### Token Rejected

**Problem:** `GitHub rejected the githubToken: it is wrong, expired or revoked.`

**Solution:** Give a new token: at a terminal, updaemon asks for one; otherwise run `updaemon secret-set github githubToken`.

### No Matching Asset, or Several

**Problem:** `No asset in the release ... is named *-linux-arm64.zip, *-linux-arm64.tar.gz or *-linux-arm64` or `Several assets in the release ... match '*.zip'`

**Solution:** The error lists the release's assets. Set the remote to `owner/repo/<pattern>` with a pattern that matches exactly one of them, for example `owner/repo/*-linux-arm64-self-contained.zip`.

### Version Not Detected

**Problem:** Service update says "No version detected"

**Solution:** Check that the repository has at least one release. GitHub tags without releases are not detected.

## Example: Real-World Usage

Here's a complete example deploying an application via GitHub releases:

```bash
# 1. Install and configure the plugin (one-time setup)
updaemon dist-install github
updaemon secret-set github githubToken

# 2. Install the latest release of your application, built for this machine
updaemon install myapp --from github:your-org/myapp

# 3. Later releases are installed by the update timer, or by hand
updaemon update myapp
```

## Contributing

This plugin is part of the Updaemon project. For contribution guidelines, see the main repository.

## License

[Same as Updaemon]


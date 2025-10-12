# Updaemon.Distribution.ByteShelfDistribution

A distribution plugin for Updaemon that fetches versioned releases from ByteShelf storage.

## Overview

This plugin enables Updaemon to download and manage applications distributed via ByteShelf storage. It uses a hierarchical folder structure where subtenants represent applications, sub-subtenants represent versions, and files within version folders are the actual distribution files.

## Features

- **Hierarchical Version Storage** - Uses ByteShelf's subtenant structure for organizing versions
- **Automatic Version Detection** - Parses version information from subtenant names (supports `v1.2.3`, `1.2.3`, and other formats)
- **Asset Pattern Matching** - Use wildcards to select specific files from version folders with multiple files
- **Secure Authentication** - API key authentication for accessing private ByteShelf instances
- **Automatic Extraction** - Automatically extracts and unwraps `.zip` archives
- **AOT Compiled** - Fast startup with no runtime dependencies

## ByteShelf Structure Requirements

The plugin expects the following hierarchy in ByteShelf:

```
Root Tenant (authenticated with API key)
└── App Subtenant (e.g., "MyApp")
    └── Version Sub-subtenant (e.g., "1.0.2", "v1.2.3")
        ├── linux-arm64.zip
        ├── linux-x64.zip
        └── windows-x64.zip
```

## Installation

```bash
sudo updaemon dist-install https://github.com/AdamTovatt/updaemon/releases/download/v0.3.1/Updaemon.Distribution.ByteShelfDistribution
```

## Configuration

> [!NOTE]
> You need to configure two secrets for the plugin to work.

```bash
sudo updaemon secret-set byteShelfUrl https://your-byteshelf-instance.com
```

```bash
sudo updaemon secret-set byteShelfApiKey your-api-key-here
```

The API key must have access to your files.

### Service Name Format

The service / remote name determines which ByteShelf subtenant and file to download:

**Format: `AppSubtenant` or `AppSubtenant/pattern`**
- `AppSubtenant` - The name of the subtenant containing version folders
- `pattern` (optional) - Wildcard pattern to match specific files (`*` matches anything, `?` matches one character)

**Examples:**
- `MyApp` - Downloads the single file from the latest version of MyApp
- `MyApp/linux-*.zip` - Downloads the file matching `linux-*.zip` from the latest version

The tenant that the api key leads to is expected to have `MyApp` in it. Then, inside that version folders (subsubtenants) are expected to exist. Then, for each version there can be one or multiple actual release assets.

## Usage

### Example: Basic Setup

```bash
# 1. Set up ByteShelf credentials
sudo updaemon secret-set byteShelfUrl https://byteshelf.example.com
sudo updaemon secret-set byteShelfApiKey abc123xyz

# 2. Create a new service
sudo updaemon new myapp

# 3. Set the remote to your ByteShelf app subtenant
sudo updaemon set-remote myapp MyApp

# 4. Update to the latest version
sudo updaemon update myapp
```

### Example: Pattern Matching

```bash
# Create a service
sudo updaemon new word-library-api

# Set the remote with a pattern to match platform-specific files
sudo updaemon set-remote word-library-api WordLibraryApi/linux-x64.zip

# Update to the latest version
sudo updaemon update word-library-api
```

## Service Name Patterns

### Pattern Examples

| Pattern | Matches | Use Case |
|---------|---------|----------|
| `MyApp` | Single file only | Simple releases with one file per version |
| `MyApp/*.zip` | Any `.zip` file | Version folders with mixed file types |
| `MyApp/linux-*.zip` | Files starting with `linux-` and ending with `.zip` | Platform-specific binaries |
| `MyApp/*-x64.zip` | Files ending with `-x64.zip` | Architecture-specific archives |
| `MyApp/app-?.?.?.zip` | `app-1.2.3.zip`, `app-2.0.1.zip` | Specific filename structures |

### Pattern Matching Behavior

- Patterns are **case-insensitive**
- If no pattern matches, you'll get a clear error listing all available files
- If multiple files match, you'll be prompted to use a more specific pattern

## Version Parsing

The plugin automatically extracts version numbers from subtenant names.

### Supported Formats

| Subtenant Name | Parsed Version | Notes |
|----------------|----------------|-------|
| `v1.2.3` | `1.2.3` | Standard semantic versioning with `v` prefix |
| `1.2.3` | `1.2.3` | Standard semantic versioning without prefix |
| `v1.2.3-beta` | `1.2.3` | Pre-release suffixes are stripped |
| `release-1.0.0` | `1.0.0` | Common prefixes stripped |
| `version_1_2_3` | `1.2.3` | Underscores converted to dots |

### Version Parsing Behavior

- The parser tries to extract version numbers from any format
- Non-numeric prefixes and suffixes are automatically stripped
- Underscores are treated as version delimiters
- Sub-subtenants that cannot be parsed as versions are skipped with a console warning
- The latest version (highest version number) is always selected

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
- `ByteShelfDistributionService` - Main service implementation
- `ByteShelfApiClient` - ByteShelf API communication layer using HttpShelfFileProvider
- `VersionParser` - Extracts versions from subtenant names (shared from Updaemon.Common)
- `DownloadPostProcessor` - Handles zip extraction and unwrapping (shared from Updaemon.Common)

For more details on creating distribution plugins, see [Updaemon.Common/README.md](../Updaemon.Common/README.md).

## Troubleshooting

### Missing Credentials

**Problem:** `ByteShelf API key is required` or `ByteShelf URL is required`

**Solution:** Set up both required secrets:
```bash
sudo updaemon secret-set byteShelfUrl https://your-byteshelf-instance.com
sudo updaemon secret-set byteShelfApiKey your-api-key-here
```

### App Subtenant Not Found

**Problem:** `Warning: App subtenant 'MyApp' not found in ByteShelf`

**Solution:** 
1. Verify the subtenant name matches exactly (case-insensitive comparison is used)
2. Check that your API key has access to the tenant containing the subtenant
3. Log into your ByteShelf instance and verify the subtenant exists

### No Version Subtenants Found

**Problem:** `Warning: No version subtenants found under 'MyApp'`

**Solution:**
1. Verify that you have created version subtenants under your app subtenant
2. Check that version subtenants are named with version numbers (e.g., `1.0.0`, `v1.2.3`)

### Version Not Detected

**Problem:** `Warning: Skipping subtenant 'docs' - not a valid version`

**Solution:** This is informational. The plugin skips subtenants that don't parse as versions. If a version subtenant is being skipped:
1. Check that the subtenant name contains version numbers
2. Use formats like `1.0.0`, `v1.2.3`, or `release-1.0.0`

### No Matching Files

**Problem:** `No files matching pattern 'xyz' found`

**Solution:** 
1. Check what files exist in your version subtenant
2. Adjust your pattern to match the actual filenames
3. Use wildcards appropriately (`*` for any characters, `?` for single character)

### Multiple Files Match

**Problem:** `Multiple files match pattern '*.zip'`

**Solution:** Use a more specific pattern. For example:
- If `*.zip` matches both `app-windows.zip` and `app-linux.zip`
- Use `*-linux.zip` to match only the Linux version

## Example: Complete Deployment Workflow

Here's a complete example deploying an application via ByteShelf:

```bash
# 1. Configure ByteShelf (one-time setup)
sudo updaemon dist-install https://example.com/updaemon-byteshelf-plugin
sudo updaemon secret-set byteShelfUrl https://byteshelf.example.com
sudo updaemon secret-set byteShelfApiKey your-api-key

# 2. Create the ByteShelf structure
# In ByteShelf, create:
#   Subtenant: MyApp
#     Sub-subtenant: 1.0.0
#       File: linux-x64.zip
#     Sub-subtenant: 1.0.1
#       File: linux-x64.zip

# 3. Create and configure the service
sudo updaemon new myapp
sudo updaemon set-remote myapp MyApp/linux-x64.zip

# 4. Download and install the latest version
sudo updaemon update myapp

# 5. Verify the service is running
systemctl status myapp

# 6. When you upload a new version (e.g., 1.0.2) to ByteShelf,
#    just run the update command again
sudo updaemon update myapp
```

## License

MIT (Same as Updaemon)


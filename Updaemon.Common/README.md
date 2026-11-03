# Updaemon.Common

This library contains shared code between updaemon and distribution service plugins. Plugin authors should reference this package to implement custom distribution services.

## Purpose

Updaemon uses a pluggable architecture where distribution services are separate AOT-compiled executables that communicate via named pipes using JSON-RPC. This package contains:

- **`IDistributionService`** - The interface that all distribution plugins must implement
- **RPC message types** - `RpcRequest` and `RpcResponse` for named pipe communication
- **JSON serialization context** - `CommonJsonContext` for AOT-compatible serialization
- **Utilities** - Helper classes like `DownloadPostProcessor` for common tasks

## Using This Package

### 1. Reference the Package

Add a project reference to `Updaemon.Common`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Updaemon.Common\Updaemon.Common.csproj" />
</ItemGroup>
```

Or if published as a NuGet package:

```xml
<ItemGroup>
  <PackageReference Include="Updaemon.Common" Version="1.0.0" />
</ItemGroup>
```

### 2. Implement IDistributionService

```csharp
using Updaemon.Common;

public class MyDistributionService : IDistributionService
{
    public DistributionServiceInformation GetServiceInformation()
    {
        return new DistributionServiceInformation
        {
            FullName = "My Distribution Service",
            DefaultAlias = "mydist",
            Description = "Retrieves releases from MyDist.",
            Version = "1.0.0",
            Secrets = new List<DistributionSecretInfo>
            {
                new DistributionSecretInfo { Name = "apiKey", IsRequired = true, Description = "API key for authentication" },
            },
        };
    }
    public Task InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default)
    {
        // Access secrets using secrets.GetValue("key") or secrets.GetValueIgnoreCase("key")
        // Initialize your distribution service client
    }

    public Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        // Query your distribution server for the latest version
        // Return null if service doesn't exist
    }

    public Task<DownloadResult> DownloadVersionAsync(string serviceName, Version version, string runtimeIdentifier, string targetPath, CancellationToken cancellationToken = default)
    {
        // Download the build of the specified version for runtimeIdentifier to targetPath
        // Verify it against a published checksum if there is one, then extract/copy all necessary files
        // Return new DownloadResult { Verified = <whether it was checked against a checksum> }
    }
}
```

### 3. Host the Service

Use the `DistributionServiceHost` helper to handle all named pipe infrastructure:

```csharp
using Updaemon.Common;
using Updaemon.Common.Hosting;

class Program
{
    static async Task Main(string[] args)
    {
        IDistributionService service = new MyDistributionService();
        await DistributionServiceHost.RunAsync(args, service);
    }
}
```

**That's it!** The host automatically:
- Parses the `--pipe-name` argument
- Creates and manages the named pipe server
- Handles the RPC request/response loop
- Routes calls to your `IDistributionService` implementation
- Converts exceptions to RPC error responses carrying the exception's message only

Updaemon shows that message to the user as is, with no stack trace, so make it say what went wrong and what to change (e.g. `Invalid GitHub remote 'myapp': expected owner/repo or owner/repo/<pattern>.`).

## Plugin Protocol Version

The service information a plugin returns carries `ProtocolVersion`, the `PluginProtocol.Version` of the Updaemon.Common it was built against; plugins do not set it. Updaemon refuses a plugin whose protocol version differs from its own and tells the user to reinstall it. Rebuild your plugin against the matching Updaemon.Common when the protocol changes.

## IDistributionService Interface

### GetServiceInformation()

Returns static metadata about the plugin (full name, default alias, version, description, and required/optional secrets). This should not perform network operations. Updaemon calls it each time it starts the plugin, to check the protocol version.

### InitializeAsync(SecretCollection secrets, CancellationToken cancellationToken = default)

Called once when updaemon connects to your plugin.

**Parameters:**
- `secrets` - Collection of secret key-value pairs. Use `GetValue(key)` for case-sensitive lookup or `GetValueIgnoreCase(key)` for case-insensitive lookup.
- `cancellationToken` - Cancellation token to cancel the operation.

**Example usage:**
```csharp
string? tenantId = secrets.GetValueIgnoreCase("tenantId");
string? apiKey = secrets.GetValueIgnoreCase("apiKey");

// Or using TryGet pattern
if (secrets.TryGetValue("githubToken", out string? token))
{
    // Use token
}
```

### GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)

Query your distribution server for the latest available version.

**Parameters:**
- `serviceName` - The remote service name (e.g., "FastPackages.WordLibraryApi")
- `cancellationToken` - Cancellation token to cancel the operation.

**Returns:**
- `Version?` - The latest version available, or `null` if the service doesn't exist

### DownloadVersionAsync(string serviceName, Version version, string runtimeIdentifier, string targetPath, CancellationToken cancellationToken = default)

Download a specific version to the target directory.

**Parameters:**
- `serviceName` - The remote service name
- `version` - The specific version to download (e.g., `new Version(1, 2, 3)`)
- `runtimeIdentifier` - The runtime identifier of the machine updaemon runs on (e.g., `linux-arm64`, `osx-arm64`), for picking the build that runs there. A source that holds one build per version can ignore it.
- `targetPath` - The directory path where files should be downloaded (e.g., `/opt/my-app/1.2.3/`)
- `cancellationToken` - Cancellation token to cancel the operation.

**Returns:**
- `DownloadResult` - `Verified` says whether the download was checked against a checksum its source published. Updaemon tells the user when it was not.

**Behavior:**
- Download all necessary files for the service
- Verify the download before extracting it when the source publishes a checksum (`Sha256Sums` reads a `SHA256SUMS` file), and throw on a mismatch
- Extract archives if needed (see `DownloadPostProcessor` utility below)
- Preserve file permissions (especially for executables)
- Throw exceptions on failure
- Respect cancellation token for long-running operations

## Utilities

### DownloadPostProcessor

A helper utility for automatically extracting and unwrapping downloaded archives. This is useful when your distribution service downloads `.zip` or `.tar.gz` files.

**Features:**
- Detects and extracts a lone `.zip` or `.tar.gz` file, keeping a tar entry's file mode
- Unwraps single-directory structures (e.g., if a zip contains only `app-1.0.0/` with files inside, moves files up one level)
- Deletes the archive file after extraction
- Throws `InvalidOperationException` when the archive cannot be extracted, including when an entry or link would land outside the target directory

**Usage Example:**

```csharp
using Updaemon.Common;
using Updaemon.Common.Utilities;

public class MyDistributionService : IDistributionService
{
    private readonly IDownloadPostProcessor _postProcessor = new DownloadPostProcessor();

    public async Task<DownloadResult> DownloadVersionAsync(string serviceName, Version version, string runtimeIdentifier, string targetPath, CancellationToken cancellationToken = default)
    {
        // Download the zip file from your distribution server
        string zipPath = Path.Combine(targetPath, $"{serviceName}-{version}.zip");
        await DownloadZipFileAsync(serviceName, version, zipPath, cancellationToken);
        
        // Automatically extract and unwrap
        await _postProcessor.ProcessAsync(targetPath, cancellationToken);
        
        // Files are now ready for use; this source publishes no checksums
        return new DownloadResult { Verified = false };
    }
}
```

**When to Use:**
- Your distribution source provides `.zip` or `.tar.gz` archives
- You want automatic extraction and directory unwrapping
- You need consistent behavior across different archive structures

**When NOT to Use:**
- You download pre-extracted files directly
- You need custom extraction logic
- You use other archive formats (implement your own logic)

### Sha256Sums

Reads a release's `SHA256SUMS` file, in `sha256sum`'s format, and checks a download against it.

- `Sha256Sums.AssetName` - The file's name, `SHA256SUMS`
- `Sha256Sums.Parse(text)` - Reads the file. Lines in another form are skipped, and a name is matched without its directory, so `sha256sum dist/*` output works
- `VerifyAsync(filePath, assetName)` - Throws `ChecksumException` when there is no well-formed line for `assetName`, its lines disagree, or the file does not match

Verify before extracting, so a tampered archive is never opened.

## RPC Protocol

Communication between updaemon and your plugin uses JSON-RPC over named pipes. Right after connecting, updaemon calls `GetServiceInformation` and checks the protocol version before any other call. `DistributionServiceHost` handles all of this; the format below is for reference.

### Request Format

```json
{
  "id": "unique-request-id",
  "method": "GetLatestVersionAsync",
  "parameters": "\"MyApp\""
}
```

`parameters` is itself JSON: a string for `GetLatestVersionAsync`, a key-value object of secrets for `InitializeAsync`, and for `DownloadVersionAsync` a `DownloadVersionParameters` object:

```json
"{\"serviceName\":\"MyApp\",\"version\":\"1.2.3\",\"runtimeIdentifier\":\"linux-arm64\",\"targetPath\":\"/opt/my-app/1.2.3\"}"
```

Its result is a `DownloadResult` object: `"{\"verified\":true}"`.

### Response Format

```json
{
  "id": "unique-request-id",
  "success": true,
  "result": "\"1.2.3\"",
  "error": null
}
```

## AOT Compilation

Both updaemon and plugins use AOT compilation for fast startup. This package is designed to be AOT-compatible:

- Uses `CommonJsonContext` for source-generated serialization
- No runtime reflection
- All types are trim-safe

Enable AOT in your plugin project:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

## Publishing a plugin on GitHub

Users install a plugin from your repository's releases with `updaemon dist-install github:<owner>/<repo>`, and while self-update is on, each `updaemon update` installs your newer releases. For that, each release must have:

- One asset per platform named `<name>-<rid>`, such as `my-plugin-linux-arm64`, `my-plugin-linux-x64` and `my-plugin-osx-arm64`: the AOT binary itself, not an archive. Exactly one asset may end in `-<rid>` for each platform; a second one fails installs and updates on it.
- A `SHA256SUMS` asset listing them, as `sha256sum * > SHA256SUMS` writes it. A release without one, or whose binary does not match its line, is refused.

updaemon follows only the release GitHub marks Latest; drafts and pre-releases are never installed. Any Latest tag other than the installed one replaces it, so marking an older release Latest moves users back to it. A release refused for its checksum, its build or its plugin protocol is skipped until another release is Latest.

Build against the `Updaemon.Common` of the updaemon release your users run: updaemon refuses a plugin that speaks another protocol version (see [Versioning](#versioning)).

## Example: ByteShelf Distribution Plugin

Here's a minimal example structure for a ByteShelf distribution plugin:

```
MyPlugin/
├── MyPlugin.csproj
├── Program.cs                    # Named pipe server
├── MyDistributionService.cs      # IDistributionService implementation
└── ByteShelfClient.cs            # Your distribution API client
```

## Versioning

A breaking change to the contract between updaemon and its plugins raises `PluginProtocol.Version`, so updaemon refuses plugins built against the old contract instead of misreading them.

## License

[Same as Updaemon]


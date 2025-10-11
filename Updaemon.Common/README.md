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
    public Task InitializeAsync(string? secrets, CancellationToken cancellationToken = default)
    {
        // Parse secrets (key=value pairs separated by newlines)
        // Initialize your distribution service client
    }

    public Task<Version?> GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        // Query your distribution server for the latest version
        // Return null if service doesn't exist
    }

    public Task DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default)
    {
        // Download the specified version to targetPath
        // Extract/copy all necessary files
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
- Converts exceptions to proper RPC error responses

## IDistributionService Interface

### InitializeAsync(string? secrets, CancellationToken cancellationToken = default)

Called once when updaemon connects to your plugin.

**Parameters:**
- `secrets` - Nullable string containing zero or more `key=value` pairs separated by line breaks. Null if no secrets configured.
- `cancellationToken` - Cancellation token to cancel the operation.

**Example secrets:**
```
tenantId=550e8400-e29b-41d4-a716-446655440000
apiKey=abc123xyz
```

### GetLatestVersionAsync(string serviceName, CancellationToken cancellationToken = default)

Query your distribution server for the latest available version.

**Parameters:**
- `serviceName` - The remote service name (e.g., "FastPackages.WordLibraryApi")
- `cancellationToken` - Cancellation token to cancel the operation.

**Returns:**
- `Version?` - The latest version available, or `null` if the service doesn't exist

### DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default)

Download a specific version to the target directory.

**Parameters:**
- `serviceName` - The remote service name
- `version` - The specific version to download (e.g., `new Version(1, 2, 3)`)
- `targetPath` - The directory path where files should be downloaded (e.g., `/opt/my-app/1.2.3/`)
- `cancellationToken` - Cancellation token to cancel the operation.

**Behavior:**
- Download all necessary files for the service
- Extract archives if needed (see `DownloadPostProcessor` utility below)
- Preserve file permissions (especially for executables)
- Throw exceptions on failure
- Respect cancellation token for long-running operations

## Utilities

### DownloadPostProcessor

A helper utility for automatically extracting and unwrapping downloaded archives. This is useful when your distribution service downloads zip files.

**Features:**
- Automatically detects and extracts `.zip` files
- Unwraps single-directory structures (e.g., if a zip contains only `app-1.0.0/` with files inside, moves files up one level)
- Deletes the archive file after extraction
- Gracefully handles errors without failing the download

**Usage Example:**

```csharp
using Updaemon.Common;
using Updaemon.Common.Utilities;

public class MyDistributionService : IDistributionService
{
    private readonly IDownloadPostProcessor _postProcessor = new DownloadPostProcessor();

    public async Task DownloadVersionAsync(string serviceName, Version version, string targetPath, CancellationToken cancellationToken = default)
    {
        // Download the zip file from your distribution server
        string zipPath = Path.Combine(targetPath, $"{serviceName}-{version}.zip");
        await DownloadZipFileAsync(serviceName, version, zipPath, cancellationToken);
        
        // Automatically extract and unwrap
        await _postProcessor.ProcessAsync(targetPath, cancellationToken);
        
        // Files are now ready for use
    }
}
```

**When to Use:**
- Your distribution source provides `.zip` archives
- You want automatic extraction and directory unwrapping
- You need consistent behavior across different archive structures

**When NOT to Use:**
- You download pre-extracted files directly
- You need custom extraction logic
- You use non-zip archive formats (implement your own logic)

## RPC Protocol

Communication between updaemon and your plugin uses JSON-RPC over named pipes.

### Request Format

```json
{
  "id": "unique-request-id",
  "method": "GetLatestVersionAsync",
  "parameters": "{\"serviceName\":\"MyApp\"}"
}
```

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

This package follows semantic versioning. Breaking changes to the contract will result in a major version bump. Plugin authors should declare which contract version they support.

## License

[Same as Updaemon]


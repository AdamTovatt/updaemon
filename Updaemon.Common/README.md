# Updaemon.Common

This library contains shared code between updaemon and distribution service plugins. Plugin authors should reference this package to implement custom distribution services.

## Purpose

Updaemon uses a pluggable architecture where distribution services are separate AOT-compiled executables that communicate via named pipes using JSON-RPC. This package contains:

- **`IDistributionService`** - The interface that all distribution plugins must implement
- **RPC message types** - `RpcRequest` and `RpcResponse` for named pipe communication
- **JSON serialization context** - `CommonJsonContext` for AOT-compatible serialization

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
    public Task InitializeAsync(string? secrets)
    {
        // Parse secrets (key=value pairs separated by newlines)
        // Initialize your distribution service client
    }

    public Task<Version?> GetLatestVersionAsync(string serviceName)
    {
        // Query your distribution server for the latest version
        // Return null if service doesn't exist
    }

    public Task DownloadVersionAsync(string serviceName, Version version, string targetPath)
    {
        // Download the specified version to targetPath
        // Extract/copy all necessary files
    }
}
```

### 3. Host a Named Pipe Server

Your plugin executable must:
- Accept a `--pipe-name <name>` command-line argument
- Create a named pipe server with that name
- Handle JSON-RPC requests using `RpcRequest` and `RpcResponse`
- Use `CommonJsonContext` for serialization (AOT-compatible)

Example structure:

```csharp
using System.IO.Pipes;
using System.Text.Json;
using Updaemon.Common.Rpc;
using Updaemon.Common.Serialization;

class Program
{
    static async Task Main(string[] args)
    {
        string pipeName = GetPipeNameFromArgs(args);
        
        using var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut);
        await pipeServer.WaitForConnectionAsync();
        
        var service = new MyDistributionService();
        
        using var reader = new StreamReader(pipeServer);
        using var writer = new StreamWriter(pipeServer) { AutoFlush = true };
        
        while (pipeServer.IsConnected)
        {
            string? requestJson = await reader.ReadLineAsync();
            if (requestJson == null) break;
            
            var request = JsonSerializer.Deserialize(requestJson, CommonJsonContext.Default.RpcRequest);
            var response = await HandleRequest(service, request);
            
            string responseJson = JsonSerializer.Serialize(response, CommonJsonContext.Default.RpcResponse);
            await writer.WriteLineAsync(responseJson);
        }
    }
}
```

## IDistributionService Interface

### InitializeAsync(string? secrets)

Called once when updaemon connects to your plugin.

**Parameters:**
- `secrets` - Nullable string containing zero or more `key=value` pairs separated by line breaks. Null if no secrets configured.

**Example secrets:**
```
tenantId=550e8400-e29b-41d4-a716-446655440000
apiKey=abc123xyz
```

### GetLatestVersionAsync(string serviceName)

Query your distribution server for the latest available version.

**Parameters:**
- `serviceName` - The remote service name (e.g., "FastPackages.WordLibraryApi")

**Returns:**
- `Version?` - The latest version available, or `null` if the service doesn't exist

### DownloadVersionAsync(string serviceName, Version version, string targetPath)

Download a specific version to the target directory.

**Parameters:**
- `serviceName` - The remote service name
- `version` - The specific version to download (e.g., `new Version(1, 2, 3)`)
- `targetPath` - The directory path where files should be downloaded (e.g., `/opt/my-app/1.2.3/`)

**Behavior:**
- Download all necessary files for the service
- Extract archives if needed
- Preserve file permissions (especially for executables)
- Throw exceptions on failure

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


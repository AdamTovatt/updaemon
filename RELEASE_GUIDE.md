# Release Guide

How to create a new release for Updaemon.

## Overview

A release consists of AOT-compiled, self-contained linux-arm64 binaries uploaded as GitHub release assets. The main binary (`Updaemon`) is always included. Distribution plugin binaries are included only when their code has changed since the last release they were included in.

## Projects and their release assets

| Project | Asset name | Include when |
|---------|-----------|--------------|
| `Updaemon/Updaemon.csproj` | `Updaemon` | Always |
| `Updaemon.GithubDistributionService/Updaemon.GithubDistributionService.csproj` | `Updaemon.GithubDistributionService` | Code in that project or `Updaemon.Common` changed |
| `Updaemon.Distribution.ByteShelfDistribution/Updaemon.Distribution.ByteShelfDistribution.csproj` | `Updaemon.Distribution.ByteShelfDistribution` | Code in that project or `Updaemon.Common` changed |

Check what changed since the last release tag:
```bash
git diff v0.6.0..HEAD --stat -- Updaemon.GithubDistributionService/ Updaemon.Distribution.ByteShelfDistribution/ Updaemon.Common/
```

## Step-by-step

### 1. Bump version numbers

Update the `<Version>` element in each `.csproj` file that will be published:

- `Updaemon/Updaemon.csproj` (always)
- `Updaemon.GithubDistributionService/Updaemon.GithubDistributionService.csproj` (if included)
- `Updaemon.Distribution.ByteShelfDistribution/Updaemon.Distribution.ByteShelfDistribution.csproj` (if included)

### 2. Run tests

```bash
dotnet test
```

### 3. Build the release binaries

The publish command for each project:

```bash
dotnet publish ./Updaemon/Updaemon.csproj -c Release -r linux-arm64 --self-contained true -p:StripSymbols=true
dotnet publish ./Updaemon.GithubDistributionService/Updaemon.GithubDistributionService.csproj -c Release -r linux-arm64 --self-contained true -p:StripSymbols=true
dotnet publish ./Updaemon.Distribution.ByteShelfDistribution/Updaemon.Distribution.ByteShelfDistribution.csproj -c Release -r linux-arm64 --self-contained true -p:StripSymbols=true
```

Notes:
- `PublishAot` and `InvariantGlobalization` are already set in the `.csproj` files, so they don't need to be passed on the command line.
- Do NOT pass `-p:PublishSingleFile=true` — it is incompatible with `PublishAot`.
- AOT compilation requires native build tools. If you get linker errors about `-lz`, install `zlib1g-dev`:
  ```bash
  sudo apt-get install zlib1g-dev
  ```

The output binaries will be at:
```
Updaemon/bin/Release/net8.0/linux-arm64/native/Updaemon
Updaemon.GithubDistributionService/bin/Release/net8.0/linux-arm64/native/Updaemon.GithubDistributionService
Updaemon.Distribution.ByteShelfDistribution/bin/Release/net8.0/linux-arm64/native/Updaemon.Distribution.ByteShelfDistribution
```

### 4. Update PluginRegistry.json

`PluginRegistry.json` in the repository root maps plugin names to download URLs. When a user runs `updaemon dist-install github`, the URL is resolved from this file (fetched from the `master` branch via `GitHubPluginUrlResolver`).

Update the URLs for any plugins included in the release:

```json
{
  "github": "https://github.com/AdamTovatt/updaemon/releases/download/vX.Y.Z/Updaemon.GithubDistributionService",
  "byteshelf": "https://github.com/AdamTovatt/updaemon/releases/download/vX.Y.Z/Updaemon.Distribution.ByteShelfDistribution"
}
```

Only update entries for plugins that are being published in this release. Leave others pointing at their most recent release.

**Important:** This file must be committed and pushed to `master` before (or as part of) the release, since the resolver fetches it from `master` at runtime.

### 5. Commit, tag, and push

```bash
git add -A
git commit -m "bump version to vX.Y.Z"
git push
```

### 6. Create the GitHub release

```bash
gh release create vX.Y.Z \
  Updaemon/bin/Release/net8.0/linux-arm64/native/Updaemon \
  Updaemon.GithubDistributionService/bin/Release/net8.0/linux-arm64/native/Updaemon.GithubDistributionService \
  --title "vX.Y.Z" \
  --notes "Short description of what changed."
```

Adjust the asset list to include only the binaries being published.

## Updating installed plugins

Users who already have a plugin installed can update it with `updaemon dist-update`.

## Version history

| Release | Updaemon | GithubDist | ByteShelfDist |
|---------|----------|------------|---------------|
| v0.7.0 | 0.7.0 | 0.4.0 | - |
| v0.6.0 | 0.6.0 | - | - |
| v0.5.1 | 0.5.1 | 0.3.0 | 0.2.1 |
| v0.5.0 | 0.5.0 | 0.3.0 | 0.2.1 |
| v0.4.0 | 0.4.0 | - | - |
| v0.3.x | 0.3.x | 0.3.0 | 0.2.1 |

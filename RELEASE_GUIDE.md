# Release Guide

How to create a new release for Updaemon.

## Automated releases (tag-triggered)

Pushing a `v*` tag triggers the `Release` workflow (`.github/workflows/release.yml`), which AOT-builds every project for `linux-arm64` and `osx-arm64`, then creates the GitHub release with auto-generated notes and the binaries attached. The normal flow is therefore:

1. Bump the `<Version>` in the relevant `.csproj` file(s) (see [Step 1](#1-bump-version-numbers)).
2. If a plugin changed, update `PluginRegistry.json` and commit it to `master` (see [Step 4](#4-update-pluginregistryjson)).
3. Commit and push to `master`, then push a `vX.Y.Z` tag — the workflow does the rest.

The workflow does **not** bump versions, edit `PluginRegistry.json`, or build `linux-x64`. Use the manual steps below when you need a `linux-x64` asset or want to build/upload by hand.

## Overview

A release consists of AOT-compiled, self-contained binaries uploaded as GitHub release assets. Updaemon supports the following runtime identifiers (RIDs):

- `linux-arm64` — Raspberry Pi, ARM Linux servers
- `linux-x64` — x86_64 Linux servers (build-only target; release as needed)
- `osx-arm64` — Apple Silicon macOS

Each project produces one asset **per RID**. Asset filenames must include the RID suffix (e.g. `Updaemon-linux-arm64`, `Updaemon-osx-arm64`) so that `install.sh` and `PluginRegistry.json` can route to the correct binary at install/update time.

The main binary (`Updaemon`) is always included. Distribution plugin binaries are included only when their code has changed since the last release they were included in.

## Projects and their release assets

| Project | Asset name (with RID suffix) | Include when |
|---------|------------------------------|--------------|
| `Updaemon/Updaemon.csproj` | `Updaemon-<rid>` | Always |
| `Updaemon.GithubDistributionService/Updaemon.GithubDistributionService.csproj` | `Updaemon.GithubDistributionService-<rid>` | Code in that project or `Updaemon.Common` changed |
| `Updaemon.Distribution.ByteShelfDistribution/Updaemon.Distribution.ByteShelfDistribution.csproj` | `Updaemon.Distribution.ByteShelfDistribution-<rid>` | Code in that project or `Updaemon.Common` changed |

Check what changed since the last release tag:
```bash
git diff v0.8.1..HEAD --stat -- Updaemon.GithubDistributionService/ Updaemon.Distribution.ByteShelfDistribution/ Updaemon.Common/
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

For each project, publish once per supported RID. Apple Silicon publishes must run on a Mac (the .NET AOT compiler emits native code for the host architecture).

**On Linux (arm64 / x64):**

```bash
for RID in linux-arm64 linux-x64; do
  dotnet publish ./Updaemon/Updaemon.csproj -c Release -r $RID --self-contained true -p:StripSymbols=true
  dotnet publish ./Updaemon.GithubDistributionService/Updaemon.GithubDistributionService.csproj -c Release -r $RID --self-contained true -p:StripSymbols=true
  dotnet publish ./Updaemon.Distribution.ByteShelfDistribution/Updaemon.Distribution.ByteShelfDistribution.csproj -c Release -r $RID --self-contained true -p:StripSymbols=true
done
```

**On macOS (Apple Silicon):**

```bash
RID=osx-arm64
dotnet publish ./Updaemon/Updaemon.csproj -c Release -r $RID --self-contained true -p:StripSymbols=true
dotnet publish ./Updaemon.GithubDistributionService/Updaemon.GithubDistributionService.csproj -c Release -r $RID --self-contained true -p:StripSymbols=true
dotnet publish ./Updaemon.Distribution.ByteShelfDistribution/Updaemon.Distribution.ByteShelfDistribution.csproj -c Release -r $RID --self-contained true -p:StripSymbols=true
```

Notes:
- `PublishAot` and `InvariantGlobalization` are already set in the `.csproj` files.
- Do NOT pass `-p:PublishSingleFile=true` — it is incompatible with `PublishAot`.
- AOT compilation requires native build tools. On Linux, install `zlib1g-dev`:
  ```bash
  sudo apt-get install zlib1g-dev
  ```
- macOS: AOT publish requires the Xcode command-line tools (`xcode-select --install`).

The output binaries will be at:
```
Updaemon/bin/Release/net8.0/<rid>/native/Updaemon
Updaemon.GithubDistributionService/bin/Release/net8.0/<rid>/native/Updaemon.GithubDistributionService
Updaemon.Distribution.ByteShelfDistribution/bin/Release/net8.0/<rid>/native/Updaemon.Distribution.ByteShelfDistribution
```

Rename each to include the RID suffix before uploading (this is the convention `install.sh` relies on):

```bash
cp Updaemon/bin/Release/net8.0/linux-arm64/native/Updaemon       Updaemon-linux-arm64
cp Updaemon/bin/Release/net8.0/linux-x64/native/Updaemon         Updaemon-linux-x64
cp Updaemon/bin/Release/net8.0/osx-arm64/native/Updaemon         Updaemon-osx-arm64
```

### 4. Update PluginRegistry.json

`PluginRegistry.json` in the repository root maps plugin names to per-RID download URLs. When a user runs `updaemon dist-install github`, the URL is resolved from this file (fetched from the `master` branch via `GitHubPluginUrlResolver`) using the running platform's RID.

Update the URLs for any plugins included in the release. Only include RIDs you actually built and uploaded — and keep each plugin's RID list complete so users on every supported platform can install. Only update entries for plugins that are being published in this release; leave others pointing at their most recent release.

```json
{
  "github": {
    "linux-arm64": "https://github.com/AdamTovatt/updaemon/releases/download/vX.Y.Z/Updaemon.GithubDistributionService-linux-arm64",
    "linux-x64":   "https://github.com/AdamTovatt/updaemon/releases/download/vX.Y.Z/Updaemon.GithubDistributionService-linux-x64",
    "osx-arm64":   "https://github.com/AdamTovatt/updaemon/releases/download/vX.Y.Z/Updaemon.GithubDistributionService-osx-arm64"
  },
  "byteshelf": {
    "linux-arm64": "https://github.com/AdamTovatt/updaemon/releases/download/vX.Y.Z/Updaemon.Distribution.ByteShelfDistribution-linux-arm64",
    "linux-x64":   "https://github.com/AdamTovatt/updaemon/releases/download/vX.Y.Z/Updaemon.Distribution.ByteShelfDistribution-linux-x64",
    "osx-arm64":   "https://github.com/AdamTovatt/updaemon/releases/download/vX.Y.Z/Updaemon.Distribution.ByteShelfDistribution-osx-arm64"
  }
}
```

**Important:** This file must be committed and pushed to `master` before (or as part of) the release, since the resolver fetches it from `master` at runtime.

### 5. Commit, tag, and push

```bash
git add -A
git commit -m "bump version to vX.Y.Z"
git push
```

### 6. Create the GitHub release

Upload one asset per RID per project that's being published. The asset list should mirror what you produced in Step 3 — if you built `Updaemon` for three RIDs, upload three `Updaemon-<rid>` files; same for each plugin. The `dist-install` resolver will fail with a clear error if a user's RID is missing from the registry but listed in `PluginRegistry.json`, so prefer keeping the lists consistent.

```bash
gh release create vX.Y.Z \
  Updaemon-linux-arm64 \
  Updaemon-linux-x64 \
  Updaemon-osx-arm64 \
  Updaemon.GithubDistributionService-linux-arm64 \
  Updaemon.GithubDistributionService-linux-x64 \
  Updaemon.GithubDistributionService-osx-arm64 \
  Updaemon.Distribution.ByteShelfDistribution-linux-arm64 \
  Updaemon.Distribution.ByteShelfDistribution-linux-x64 \
  Updaemon.Distribution.ByteShelfDistribution-osx-arm64 \
  --title "vX.Y.Z" \
  --notes "Short description of what changed."
```

If a plugin isn't being republished this release, drop its rows from the `gh release create` invocation and leave its existing entries in `PluginRegistry.json` pointing at the previous tag.

## macOS notes

- `install.sh` auto-detects Apple Silicon (`Darwin-arm64`) and downloads the `osx-arm64` asset.
- `curl`-downloaded binaries are not flagged with the macOS quarantine attribute, so Gatekeeper does not block them. (If a user installs by drag-from-Finder or Safari download, they would need `xattr -d com.apple.quarantine /usr/local/bin/updaemon`.)
- macOS services use launchd LaunchDaemons under `/Library/LaunchDaemons/` with reverse-DNS labels (`com.updaemon.<service>`). The unit-file template is selected automatically at runtime via `PlatformPaths`.
- Code-signing / notarization is not currently part of the release process. Unsigned binaries run on Apple Silicon without prompts when installed via `curl`/`install.sh`.

## Updating installed plugins

Users who already have a plugin installed can update it with `updaemon dist-update`. The resolver picks the URL matching the running platform's RID.

## Version history

| Release | Updaemon | GithubDist | ByteShelfDist |
|---------|----------|------------|---------------|
| v0.10.0 | 0.10.0 | 0.4.0 | 0.2.1 |
| v0.9.0 | 0.9.0 | 0.4.0 | 0.2.1 |
| v0.8.1 | 0.8.1 | 0.4.0 | 0.2.1 |
| v0.7.0 | 0.7.0 | 0.4.0 | - |
| v0.6.0 | 0.6.0 | - | - |
| v0.5.1 | 0.5.1 | 0.3.0 | 0.2.1 |
| v0.5.0 | 0.5.0 | 0.3.0 | 0.2.1 |
| v0.4.0 | 0.4.0 | - | - |
| v0.3.x | 0.3.x | 0.3.0 | 0.2.1 |

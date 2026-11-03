# Release Guide

How to create a new release for Updaemon.

## Automated releases (tag-triggered)

Pushing a `v*` tag triggers the `Release` workflow (`.github/workflows/release.yml`), which AOT-builds every project for `linux-arm64` and `osx-arm64`, then creates the GitHub release with auto-generated notes and the binaries attached. The normal flow is therefore:

1. Bump the `<Version>` in the relevant `.csproj` file(s) (see [Step 1](#1-bump-version-numbers)).
2. Commit and push to `master`, then push a `vX.Y.Z` tag — the workflow does the rest.

The workflow does **not** bump versions or build `linux-x64`. Use the manual steps below when you need a `linux-x64` asset or want to build/upload by hand.

## Overview

A release consists of AOT-compiled, self-contained binaries uploaded as GitHub release assets. Updaemon supports the following runtime identifiers (RIDs):

- `linux-arm64` — Raspberry Pi, ARM Linux servers
- `linux-x64` — x86_64 Linux servers (build-only target; release as needed)
- `osx-arm64` — Apple Silicon macOS

Each project produces one asset **per RID**. Asset filenames must include the RID suffix (e.g. `Updaemon-linux-arm64`, `Updaemon-osx-arm64`) so that `install.sh` and updaemon itself can route to the correct binary at install/update time.

For every RID a release carries, it includes every project's binary. Updaemon installs its first-party plugins from its own release (`releases/download/v<version>/<Plugin>-<rid>`), so a release with `Updaemon-<rid>` but no plugin build for that RID leaves that version unable to install the plugin there. A release without `linux-x64` at all is fine: updaemon on such a machine reports that the release has no plugin built for it.

## Projects and their release assets

| Project | Asset name (with RID suffix) |
|---------|------------------------------|
| `Updaemon/Updaemon.csproj` | `Updaemon-<rid>` |
| `Updaemon.GithubDistributionService/Updaemon.GithubDistributionService.csproj` | `Updaemon.GithubDistributionService-<rid>` |
| `Updaemon.Distribution.ByteShelfDistribution/Updaemon.Distribution.ByteShelfDistribution.csproj` | `Updaemon.Distribution.ByteShelfDistribution-<rid>` |

## Step-by-step

### 1. Bump version numbers

Update the `<Version>` element in `Updaemon/Updaemon.csproj`, and in a plugin's `.csproj` when its code changed:

- `Updaemon/Updaemon.csproj`
- `Updaemon.GithubDistributionService/Updaemon.GithubDistributionService.csproj`
- `Updaemon.Distribution.ByteShelfDistribution/Updaemon.Distribution.ByteShelfDistribution.csproj`

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

### 4. Plugin registry (only when adding a plugin)

`Updaemon/PluginAssets.json` maps each first-party plugin's name to its asset base name. It is compiled into the updaemon binary, so `updaemon dist-install github` resolves to `releases/download/v<running version>/Updaemon.GithubDistributionService-<rid>` with no network lookup. It holds no URLs or versions, so a release needs no edit to it; add an entry only when a new first-party plugin is added to the release workflow.

`PluginRegistry.json` at the repository root is the old per-RID URL registry. Updaemon 0.x fetches it from `master` on every `dist-install` and `dist-update`, so leave it unchanged.

```json
{
  "github": "Updaemon.GithubDistributionService",
  "byteshelf": "Updaemon.Distribution.ByteShelfDistribution"
}
```

### 5. Commit, tag, and push

```bash
git add -A
git commit -m "bump version to vX.Y.Z"
git push
```

### 6. Create the GitHub release

Upload one asset per RID for every project. The asset list should mirror what you produced in Step 3 — if you built `Updaemon` for three RIDs, upload three `Updaemon-<rid>` files, and the same three for each plugin, since that updaemon version installs its plugins from this release.

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

## macOS notes

- `install.sh` auto-detects Apple Silicon (`Darwin-arm64`) and downloads the `osx-arm64` asset.
- `curl`-downloaded binaries are not flagged with the macOS quarantine attribute, so Gatekeeper does not block them. (If a user installs by drag-from-Finder or Safari download, they would need `xattr -d com.apple.quarantine /usr/local/bin/updaemon`.)
- macOS services use launchd plists with reverse-DNS labels (`com.updaemon.<app>`): LaunchDaemons under `/Library/LaunchDaemons/` system-wide, LaunchAgents under `~/Library/LaunchAgents/` for a user. Updaemon generates them in code.
- Code-signing / notarization is not currently part of the release process. Unsigned binaries run on Apple Silicon without prompts when installed via `curl`/`install.sh`.

## Updating installed plugins

Users who already have a plugin installed can update it with `updaemon dist-update`. A registry plugin is fetched from the running updaemon's release, built for the machine's RID.

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

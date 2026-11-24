[<-- Back to main README.md](README.md)

# Publishing with Updaemon

How to publish an app so that `updaemon install` can set it up and `updaemon update` can keep it current.

## Release assets

Publish one asset per platform, named after the platform's .NET runtime identifier (RID):

- `<name>-<rid>.zip` or `<name>-<rid>.tar.gz`: an archive, extracted on install with its file modes. If it holds a single top-level directory, that directory becomes the release root. An archive that cannot be extracted, or has an entry that would land outside it, fails the install.
- `<name>-<rid>`: a bare executable. It cannot carry an `updaemon.json`, so it is installed with `--command` or `--service` (see [Releases without a manifest](#releases-without-a-manifest)).

For example `byteshelf-linux-arm64.zip`, `byteshelf-linux-x64.zip` and `byteshelf-osx-arm64.zip`. Updaemon picks the asset for the machine it runs on. If a release has no asset for that RID, or several, the install fails and lists the assets; the remote can then name a pattern instead, as in `github:owner/repo/<pattern>`.

Also publish a `SHA256SUMS` asset: one `<sha-256>  <asset name>` line per asset, as `sha256sum * > SHA256SUMS` writes it (a directory before the name, as in `dist/app-linux-x64.zip`, is ignored). Updaemon then checks the download against it before installing and refuses one that does not match. A `SHA256SUMS` without a line for the asset fails before anything is downloaded. Without a `SHA256SUMS`, `install` and `update` still install the release but say it was not verified.

Versions come from the release tag, for example `v1.2.0`.

## On a ByteShelf shelf

The `byteshelf` plugin reads a ByteShelf 2.0 shelf. Put each version in a directory named like a release tag, holding the same assets and `SHA256SUMS`:

```
/myapp/1.2.0/myapp-linux-arm64.zip
/myapp/1.2.0/myapp-linux-x64.zip
/myapp/1.2.0/SHA256SUMS
/myapp/1.3.0/...
```

Install with `--from byteshelf:<shelf>/myapp`, or `byteshelf:<shelf>/myapp/<pattern>` to pick the asset. The highest version is the latest; directories not named as a version are ignored. The plugin needs the secrets `byteShelfUrl` and `byteShelfKey`, a key for that shelf; a read key is enough.

## The manifest: `updaemon.json`

Put `updaemon.json` at the release root. It says what the app is and how to run it:

```json
{
  "executable": "bin/byteshelf",
  "command": "byteshelf",
  "service": { "args": ["sync", "run"] },
  "settings": {
    "BYTESHELF_URL": { "description": "Server URL", "required": true },
    "BYTESHELF_API_KEY": { "description": "API key", "required": true, "secret": true },
    "BYTESHELF_LOG_LEVEL": { "description": "Log level", "default": "info" }
  }
}
```

| Field | Meaning |
|---|---|
| `executable` | Required. Path to the executable, relative to the release root. |
| `command` | Optional. The app is a command, put on PATH under this name. |
| `service` | Optional. The app is a service, run with `args` (default: none). `healthCheckSeconds` (1 to 300, default 10) is how long it must keep running after a start or restart. |
| `settings` | Optional. Environment variables the app reads, by name. |
| `extensions` | Optional. The app takes [extensions](#extensions). |

At least one of `command` and `service` is required; an app can be both. Each setting can have:

| Field | Meaning |
|---|---|
| `description` | Shown when updaemon asks for the value. |
| `required` | `true` if the app cannot run without it. Default `false`. |
| `secret` | `true` to store the value where only the user the app runs as can read it. Default `false`. |
| `default` | The value when none is set. A setting with a default is never required. |

An invalid manifest stops the install or update with a message naming the problem. No value may contain a line break or other control character.

The `executablePath` format of updaemon 0.x is not read any more: replace `executablePath` with `executable`, and add `command`, `service` or both.

## What updaemon does with it

- **Command**: puts a small script named after `command` on PATH. It loads the app's settings, sets the variables updaemon sets itself, then runs the executable with the arguments it was given, in the caller's working directory. It loads the secrets only for the user the app runs as: root for an app installed system-wide, unless it was installed with `--run-as <user>`.
- **Service**: writes a systemd unit (Linux) or launchd plist (macOS) that starts the same kind of script with `service.args`. The script loads the app's settings and secrets with `/bin/sh`, sets the variables updaemon sets itself, then runs the executable; the service restarts if it stops. Its working directory is the release root, which is replaced on each update.
- **Settings**: `updaemon install` asks for each required setting that has no value; `updaemon set` changes them later. Secrets are kept in a file only the user the app runs as can read.
- **Data**: every app gets a directory that is kept across updates, passed in `UPDAEMON_DATA_DIR`. Store state there.
- **Extensions**: an app that takes extensions gets the folder they are in, passed in the variable it declares.

`UPDAEMON_DATA_DIR` and the variable for extensions are set last, so no setting takes their place, and neither can be declared in `settings`.

After an update restarts a service, it must keep running for `service.healthCheckSeconds` (10 seconds unless set). Give a service that takes longer to fail, or to settle, a longer window. If it does not keep running, updaemon runs the previous version again and skips the new one until a newer release appears. Extensions switched with the app go back and are skipped with it: updaemon does not work out which one was to blame. If the switch itself fails or is cancelled, everything goes back and nothing is skipped. A first install runs the same check; with nothing to go back to, the app stays installed and `updaemon install` fails, pointing to `updaemon logs <app>`. So do `updaemon set`, `unset` and `set-args`, which restart the service whether or not it runs (on Linux, unless its unit was disabled): the change stays, and the command fails pointing to the logs.

Each update reads the new release's manifest, so a release can add settings or change its arguments. If it adds a required setting the app has no value for, the update waits: the current version keeps running and updaemon names the `updaemon set` command to run. A release without `extensions` waits the same way while any extension is installed, and updaemon names the `updaemon remove` commands.

## Extensions

An extension is a folder of files that an installed app, its host, loads. Updaemon installs it as `<app>/<extension>`, such as `byteshelf/images`, and updates it together with the host. Code for a host's extensions arrives only through updaemon; a host does not install its own.

### In the host's manifest

```json
"extensions": {
  "variable": "BYTESHELF_PLUGINS",
  "check": ["--check-plugin"],
  "known": { "images": "github:owner/byteshelf/byteshelf-images.zip" }
}
```

| Field | Meaning |
|---|---|
| `variable` | Required. The environment variable that carries the folder of extensions, for the service and the command. It is always set, and the folder can be empty. It cannot be in `settings` too, nor be `UPDAEMON_DATA_DIR`, and `updaemon set` and `unset` refuse it. A value stored for it before the host declared it is dropped when that version becomes current. |
| `check` | Optional. Arguments to the host's executable that make it [say whether it would load an extension](#the-check). Without it, the host accepts every extension. |
| `known` | Optional. Where extensions come from, as `<plugin>:<remote>` by id, so `updaemon install byteshelf/images` needs no `--from`. The id follows the rules of an app name: letters, digits, `.`, `_` and `-`, starting with a letter or digit, and not `updaemon`. Ids that differ only in case are one. An installed extension keeps the source it was installed from. |

Updaemon restarts the host when its extensions change, unless the host is set to manual restart or, on Linux, its service is disabled. A host left running keeps the files of an extension that is removed, but not for good those of one that is updated: after enough updates without a restart, the version it started with is deleted (see `releaseRetentionCount` in the README).

### What a host must do

- Load every folder directly under the path in the variable, following links.
- Resolve the links once at startup, as they change under a running host.
- Take nothing from the folder's name.
- Skip an extension it cannot load and stay up.
- Keep `check` free of effects and not need root or settings.

### The check

Updaemon runs the executable of the host version that would run, with the `check` arguments and then the extension's folder as the last one. Exit code 0 means the host would load the extension. Anything else, or no answer in 60 seconds, is a refusal, and what the check prints is the reason: its stderr, then its stdout, on one line and cut at 1000 characters. The check has answered when it exits; what it leaves running is not waited for.

- It runs as the user the host runs as, in that host version's folder. It gets no settings or secrets, no input, and no environment but a standard `PATH` and that user's `HOME`.
- The folder's name carries no meaning: the check sees a version's folder, while at startup the folder is named after the extension. What the extension is must come from the files inside.
- `install`, `update` and `rollback` of an extension run it on the version to switch to. `update` runs it on each extension, also when there is nothing to update, with the host version that will run. `rollback` of the host runs it on each extension, and goes ahead when one is refused.

### Publishing an extension

Publish an extension as an app is published: an asset per platform or one the remote names, and a `SHA256SUMS`. Its archive needs no `updaemon.json`, and one in it is ignored. Its files get the owner and permissions the host's own have: a host that runs system-wide as a user of its own only reads them.

An extension in its host's own repository must not have an asset that ends in `-<rid>.zip`, `-<rid>.tar.gz` or `-<rid>`, as updaemon would then find several for the host. Name it otherwise, such as `byteshelf-images.zip`, and name it in the remote: `github:owner/byteshelf/byteshelf-images.zip`. Two things follow from that. The remote reads the repository's latest release, so every release of the host must carry the extension's asset, or updating the extension fails. And it names the same asset on every platform, so an extension with a build per platform goes in a repository of its own, with assets named as an app's are.

An extension has no settings, data or service of its own. A value it reads from the environment can be set on the host with `updaemon set`, which takes keys the host does not declare.

## Releases without a manifest

A release without `updaemon.json` can still be installed by saying what it is:

```bash
updaemon install my-tool --from github:owner/my-tool --command my-tool
updaemon install my-api --from github:owner/my-api --service
```

Updaemon finds the executable by name: the command name, or the app name for a service. If a later release ships a manifest, that manifest is used from then on.

## Installing

```bash
updaemon install byteshelf --from github:owner/byteshelf --set BYTESHELF_URL=https://shelf.example.com
```

- `--set KEY=VALUE` gives a setting's value; repeat it for more. Updaemon asks for each required setting that is still missing, without echo for secrets. Without a terminal (in a script), a missing required setting fails the install instead.
- Run as root, updaemon installs system-wide; run as any other user, it installs for that user only. `--run-as <user>` (system-wide only) runs the service as that user instead of root.
- On Linux, a user's services run only while that user is logged in, unless lingering is on: `loginctl enable-linger <user>`.

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

- **Command**: puts a small script named after `command` on PATH. It loads the app's settings, then runs the executable with the arguments it was given, in the caller's working directory. It loads the secrets only for the user the app runs as: root for an app installed system-wide, unless it was installed with `--run-as <user>`.
- **Service**: writes a systemd unit (Linux) or launchd plist (macOS) that starts the same kind of script with `service.args`. The script loads the app's settings and secrets with `/bin/sh`, then runs the executable; the service restarts if it stops. Its working directory is the release root, which is replaced on each update.
- **Settings**: `updaemon install` asks for each required setting that has no value; `updaemon set` changes them later. Secrets are kept in a file only the user the app runs as can read.
- **Data**: every app gets a directory that is kept across updates, passed in `UPDAEMON_DATA_DIR`. Store state there.

After an update restarts a service, it must keep running for `service.healthCheckSeconds` (10 seconds unless set). Give a service that takes longer to fail, or to settle, a longer window. If it does not keep running, updaemon runs the previous version again and skips the new one until a newer release appears. A first install runs the same check; with nothing to go back to, the app stays installed and `updaemon install` fails, pointing to `updaemon logs <app>`. So do `updaemon set`, `unset` and `set-args` when they restart a running service: the change stays, and the command fails pointing to the logs.

Each update reads the new release's manifest, so a release can add settings or change its arguments. If it adds a required setting the app has no value for, the update waits: the current version keeps running and updaemon names the `updaemon set` command to run.

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

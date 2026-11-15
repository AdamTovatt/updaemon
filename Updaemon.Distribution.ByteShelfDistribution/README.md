# Updaemon.Distribution.ByteShelfDistribution

The updaemon plugin for apps published on a ByteShelf 2.0 shelf. It talks only to a 2.0 server.

## Setup

```bash
updaemon dist-install byteshelf
updaemon secret-set byteshelf byteShelfUrl https://shelf.example.com
updaemon secret-set byteshelf byteShelfKey
```

These run in your user scope; run them with `sudo` for the system scope ([System-wide or per user](../README.md#system-wide-or-per-user)). `byteShelfKey` is a shelf key for the shelf the apps are on; a `read` key is enough. Signing in as a user is not supported. When the key is missing or refused, `install` and `update` ask for it at a terminal; under the timer they fail and name the `secret-set` command.

## Remote

`<shelf>/<app>`, or `<shelf>/<app>/<pattern>` to pick the asset, where `*` matches any text:

```bash
updaemon install myapp --from byteshelf:tools/myapp
updaemon install myapp --from byteshelf:tools/myapp/*-x64.zip
```

## Layout on the shelf

```
/myapp/1.2.0/myapp-linux-arm64.zip
/myapp/1.2.0/myapp-linux-x64.zip
/myapp/1.2.0/SHA256SUMS
/myapp/v1.3.0/...
```

- Each version is a directory under `/<app>/`, named like a release tag (`1.2.0` or `v1.2.0`). The highest is the latest; directories not named as a version are ignored.
- The asset is picked as on GitHub: the one file named `*-<rid>.zip`, `*-<rid>.tar.gz` or `*-<rid>`. None or several fails and lists the files.
- With a `SHA256SUMS` in the version directory, the asset must match its line, or it is not installed. Without one it is installed and reported as not verified. ByteShelf's own check of the stored file does not count as verified.

## Apps installed from ByteShelf 1.x

A 1.x remote (`byteshelf:App` or `byteshelf:App/pattern`) names no shelf, so it fails, and the 1.x secret `byteShelfApiKey` is not read any more. Set a shelf key, then install the app again:

```bash
updaemon secret-set byteshelf byteShelfKey
updaemon remove myapp
updaemon install myapp --from byteshelf:<shelf>/myapp
```

#!/bin/bash
# Installs the latest updaemon.
#   curl -fsSL https://raw.githubusercontent.com/AdamTovatt/updaemon/master/install.sh | sudo bash   # system-wide
#   curl -fsSL https://raw.githubusercontent.com/AdamTovatt/updaemon/master/install.sh | bash        # for you only
# Run as root, it installs for the system; otherwise for the user running it. Running it again
# installs the latest version the same way, also over an updaemon 0.x install. It turns on the
# hourly update timer, unless it is on already or was turned off.
set -euo pipefail
# Whatever the caller's umask, others must be able to run the system updaemon.
umask 022

REPO=https://github.com/AdamTovatt/updaemon

case "$(uname -s)-$(uname -m)" in
  Linux-aarch64|Linux-arm64) RID=linux-arm64 ;;
  Linux-x86_64)              RID=linux-x64 ;;
  Darwin-arm64)              RID=osx-arm64 ;;
  *)
    echo "Unsupported platform: $(uname -s) $(uname -m)" >&2
    echo "Supported: Linux (arm64, x64), macOS (Apple Silicon)." >&2
    exit 1
    ;;
esac

# The same paths updaemon uses for each scope.
if [ "$(id -u)" -eq 0 ]; then
  SCOPE=system
  BIN_DIR=/usr/local/bin
  case "$RID" in
    osx-*) ROOT=/usr/local/var/updaemon ;;
    *)     ROOT=/var/lib/updaemon ;;
  esac
else
  SCOPE=user
  BIN_DIR="$HOME/.local/bin"
  case "$RID" in
    osx-*) ROOT="$HOME/Library/Application Support/updaemon" ;;
    *)
      # XDG says a relative value is invalid and must be ignored.
      case "${XDG_DATA_HOME:-}" in
        /*) ROOT="$XDG_DATA_HOME/updaemon" ;;
        *)  ROOT="$HOME/.local/share/updaemon" ;;
      esac
      ;;
  esac
fi

# The /releases/latest redirect names the latest version; unlike the API, it has no rate limit.
latest_url=$(curl -fsSLI -o /dev/null -w '%{url_effective}' "$REPO/releases/latest")
VERSION=${latest_url##*/tag/v}
# Releases are tagged v and three numbers, as updaemon reads them too.
if ! [[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Could not read the latest version from $latest_url" >&2
  exit 1
fi

asset="$REPO/releases/download/v$VERSION/Updaemon-$RID"
version_dir="$ROOT/versions/$VERSION"
binary="$version_dir/updaemon"
download="$binary.$$.download"
printed_file="$download.out"

# Prints what '$1 --version' printed, giving up after 10 seconds: a build that hangs is not
# installed. Its output goes through a file, which a process it left behind cannot hold open.
smoke_run() {
  "$1" --version >"$printed_file" 2>&1 &
  local pid=$!
  ( sleep 10; kill -9 "$pid" ) >/dev/null 2>&1 &
  local watchdog=$!
  wait "$pid" || true
  kill "$watchdog" 2>/dev/null || true
  cat "$printed_file"
  rm -f "$printed_file"
}

# Leaves nothing of a failed install behind.
discard() {
  rm -f "$download" "$printed_file"
  rmdir "$version_dir" 2>/dev/null || true
}

mkdir -p "$version_dir" "$BIN_DIR"
if ! curl -fsSL -o "$download" "$asset"; then
  discard
  echo "No updaemon $VERSION build for $RID at $asset" >&2
  exit 1
fi

# Install only a build that matches its line in the release's SHA256SUMS.
if ! sums=$(curl -fsSL "$REPO/releases/download/v$VERSION/SHA256SUMS"); then
  discard
  echo "updaemon $VERSION was not installed: its release has no SHA256SUMS to verify it against." >&2
  exit 1
fi
# release.yml writes it with plain `sha256sum *`: lowercase hashes, bare names, no '*'.
expected=$(awk -v name="Updaemon-$RID" '$2 == name { print $1; exit }' <<<"$sums")
if [ -z "$expected" ]; then
  discard
  echo "updaemon $VERSION was not installed: SHA256SUMS has no line for Updaemon-$RID." >&2
  exit 1
fi
case "$RID" in
  osx-*) actual=$(shasum -a 256 "$download" | cut -d' ' -f1) ;;
  *)     actual=$(sha256sum "$download" | cut -d' ' -f1) ;;
esac
if [ "$actual" != "$expected" ]; then
  discard
  echo "updaemon $VERSION was not installed: Updaemon-$RID does not match its SHA-256 in SHA256SUMS; the download is corrupt or was tampered with." >&2
  exit 1
fi
chmod 755 "$download"

# Switch only to a build that runs.
printed=$(smoke_run "$download")
if [ "$printed" != "$VERSION" ]; then
  discard
  echo "updaemon $VERSION did not run here: 'updaemon --version' printed '$printed'." >&2
  exit 1
fi
mv -f "$download" "$binary"

# A new link renamed over the old one (or over an updaemon 0.x binary), so 'updaemon' never goes missing.
link="$BIN_DIR/updaemon"
ln -sf "$binary" "$link.new"
mv -f "$link.new" "$link"

if [ "$SCOPE" = system ] && [ "$RID" = osx-arm64 ]; then
  # launchd writes service logs here and drops them silently if the directory is missing.
  mkdir -p /var/log/updaemon
fi

echo "Installed updaemon $VERSION for the $SCOPE at $link."
if [ "$SCOPE" = user ]; then
  case ":$PATH:" in
    *":$BIN_DIR:"*) ;;
    *) echo "Add $BIN_DIR to your PATH to run 'updaemon'." ;;
  esac
  if [ "$RID" != osx-arm64 ]; then
    echo "Your services run only while you are logged in, unless lingering is on: sudo loginctl enable-linger $(id -un)"
  fi
fi

# Last, so a timer that cannot be set leaves nothing above unsaid. On by default; one that is set
# keeps its interval, and one turned off stays off.
if ! "$binary" timer 1h --keep; then
  echo "updaemon is installed, but the update timer was not turned on. Turn it on with 'updaemon timer 1h'." >&2
  exit 1
fi

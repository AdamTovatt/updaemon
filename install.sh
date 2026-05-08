#!/bin/bash
set -e

# Detect OS + arch and pick the matching release artifact RID.
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

case "$(uname -s)" in
  Darwin) CONFIG_DIR=/usr/local/var/updaemon ;;
  *)      CONFIG_DIR=/var/lib/updaemon ;;
esac

# Use the /releases/latest/download/<asset> redirect rather than api.github.com.
# The API is rate-limited to 60/hour per IP unauthenticated; the redirect URL is
# served from github.com release infrastructure and is not API-rate-limited.
# A missing asset returns 404 (which curl -f surfaces as a non-zero exit).
url="https://github.com/AdamTovatt/updaemon/releases/latest/download/Updaemon-${RID}"

if ! curl -fsSLI "$url" -o /dev/null; then
  echo "No release asset found at $url" >&2
  echo "If you're installing on a brand-new platform, the latest release may not have a build for it yet." >&2
  exit 1
fi

# Fresh Apple Silicon Macs ship without /usr/local/bin (Homebrew uses
# /opt/homebrew there), so curl -o would fail with "No such file or directory".
sudo mkdir -p /usr/local/bin
sudo curl -fL -o /usr/local/bin/updaemon "$url"
sudo chmod +x /usr/local/bin/updaemon

# Create configuration directory
sudo mkdir -p "${CONFIG_DIR}/plugins"

# macOS: launchd writes service stdout/stderr to /var/log/updaemon/<service>.{log,err.log}.
# Create the parent directory now so logs don't silently disappear on first service start.
if [ "$(uname -s)" = "Darwin" ]; then
  sudo mkdir -p /var/log/updaemon
fi

echo "Updaemon installed successfully to /usr/local/bin/updaemon"
echo "Config directory: ${CONFIG_DIR}"

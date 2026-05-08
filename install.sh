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

# Pick the asset whose URL ends with /Updaemon-<RID> exactly. Anchoring to the basename
# Updaemon- prevents matching plugin assets like Updaemon.GithubDistributionService-<RID>.
url=$(curl -s https://api.github.com/repos/AdamTovatt/updaemon/releases/latest \
  | grep '"browser_download_url"' \
  | grep -E "/Updaemon-${RID}\"" \
  | head -n1 \
  | sed 's/.*"browser_download_url": "\(.*\)".*/\1/')

if [ -z "$url" ]; then
  echo "No release asset found matching /Updaemon-${RID}." >&2
  echo "If you're installing on a brand-new platform, the latest release may not have a build for it yet." >&2
  exit 1
fi

sudo curl -L -o /usr/local/bin/updaemon "$url"
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

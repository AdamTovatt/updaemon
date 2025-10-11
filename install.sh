#!/bin/bash
set -e

url=$(curl -s https://api.github.com/repos/AdamTovatt/updaemon/releases/latest \
  | grep -m1 '"browser_download_url"' \
  | sed 's/.*"browser_download_url": "\(.*\)".*/\1/')

sudo curl -L -o /usr/local/bin/updaemon "$url"
sudo chmod +x /usr/local/bin/updaemon

# Create configuration directory
sudo mkdir -p /var/lib/updaemon/plugins

echo "Updaemon installed successfully to /usr/local/bin/updaemon"


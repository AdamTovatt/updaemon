**Add your first service:**
```bash
sudo updaemon new my-app
sudo updaemon set-remote my-app user-name/repo-name
```

**Update your service:**
```bash
sudo updaemon update my-app
```

That's it! Your service is now managed by Updaemon and will be automatically updated.

This will:
- Create `/opt/my-api/` directory
- Generate systemd unit file at `/etc/systemd/system/my-api.service`
- Enable the service
- Register it in updaemon's configuration

## Scheduling Updates

Set up automatic updates to run on a schedule using the built-in timer command:

```bash
# Set timer to run every 10 minutes
sudo updaemon timer 10m

# Set timer to run every hour
sudo updaemon timer 1h

# Check current timer status
sudo updaemon timer

# Disable automatic updates
sudo updaemon timer -
```

**Supported time formats:**
- `30s` - 30 seconds
- `5m` - 5 minutes  
- `1h` - 1 hour

The timer command automatically creates and manages the necessary systemd service and timer files.

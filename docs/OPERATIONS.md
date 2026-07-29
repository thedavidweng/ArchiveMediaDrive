# Operations

ArchiveMediaDrive has no standalone operational service.

## Kodi

The add-on stores sources, snapshots, and caches in its Kodi profile directory. Refresh runs inside the add-on. Kodi owns logs and lifecycle.

## Jellyfin and Emby Channel mode

The server plugin stores configuration and snapshots in its plugin data directory. Each rclone operation is a short-lived loopback subprocess. There is no rclone daemon and no network listener.

## Managed Library mode

The server plugin owns one read-only rclone mount child process. The plugin:

1. validates mount prerequisites;
2. verifies the pinned rclone runtime;
3. creates the mount path;
4. starts the foreground mount;
5. verifies health;
6. registers the host library;
7. monitors and restarts within a bounded budget;
8. unmounts during shutdown, disable, or uninstall.

Users troubleshoot the feature from the host plugin dashboard and host logs. They do not configure systemd, launchd, Docker Compose, WebDAV, reverse proxies, or port forwarding for ArchiveMediaDrive.

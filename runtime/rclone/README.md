# rclone Runtime

Jellyfin and Emby plugins bootstrap a pinned official rclone release into the host plugin data directory.

Channel mode invokes:

```text
rclone rc --loopback <operation> --json <payload>
```

Managed Library mode starts:

```text
rclone mount <generated-combine-remote>: <private-mount-path> --read-only ...
```

No RC server is started. No rclone config is exposed outside the plugin data directory. Release CI must populate and verify every SHA-256 placeholder in `manifest.json` before a package can be published.

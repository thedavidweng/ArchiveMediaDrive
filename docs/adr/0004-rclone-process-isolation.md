# ADR 0004: Use a managed rclone executable instead of in-process librclone

Status: Accepted

Jellyfin and Emby invoke a pinned official rclone executable. Channel mode uses short-lived `rclone rc --loopback` calls. Managed Library mode supervises a foreground `rclone mount` child.

`librclone` remains experimental, includes a Go runtime, needs special mount builds, and has unsafe unload behavior on Windows. Process isolation reduces the host server crash radius.

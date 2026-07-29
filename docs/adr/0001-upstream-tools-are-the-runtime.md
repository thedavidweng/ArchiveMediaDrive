# ADR 0001: Reuse upstream tools at platform-appropriate seams

Status: Accepted

Kodi uses the official `internetarchive` Python package because Kodi supplies Python and its official add-on repository prohibits compiled binaries.

Jellyfin and Emby use documented Internet Archive search APIs for source membership and the official rclone executable for item file listing, public links, Range behavior, and optional mounting. The .NET implementation is contract-tested against fixtures produced by the official `ia` client.

ArchiveMediaDrive does not implement a file-transfer backend, HTTP proxy, WebDAV server, or media engine.

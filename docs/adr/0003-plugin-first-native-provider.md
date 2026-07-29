# ADR 0003: Plugin-first native provider architecture

Status: Accepted

ArchiveMediaDrive ships as Kodi, Jellyfin, and Emby extensions. A standalone WebDAV or mount service is a development tool only and is not a supported end-user architecture.

Kodi uses a Python plugin source. Jellyfin and Emby use Channel plugins and optional plugin-managed standard-library mounts. Host settings, scheduling, logging, permissions, and lifecycle are authoritative.

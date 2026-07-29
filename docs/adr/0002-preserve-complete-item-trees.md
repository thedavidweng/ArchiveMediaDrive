# ADR 0002: Preserve complete item trees

Status: Accepted

The provider contract preserves each selected Internet Archive item at `<source>/<identifier>` with exact relative paths and filenames. ArchiveMediaDrive performs no ranking, renaming, subtitle overlay, or media interpretation.

Kodi and Managed Library mode can expose the complete tree. Jellyfin and Emby Channel APIs may only render host-supported node types; adapters must preserve the complete cached tree and must never choose one media derivative over another.

# Test Matrix

## Shared contracts

- source parsing and normalization;
- item/Collection/Favorites/search result parity with official `ia` fixtures;
- deterministic raw node IDs and paths;
- atomic refresh and stale-cache behavior;
- Unicode and malicious path inputs.

## Kodi

- Kodi 21 Omega and Kodi 22 Piers Beta 1;
- macOS, Windows, Linux, Android TV where available;
- source folder navigation;
- nested item directory navigation;
- all file visibility;
- MKV, MP4, audio, and existing subtitle files;
- open, seek, pause, resume, reconnect;
- anonymous and authenticated access;
- source-only package policy.

## Jellyfin Channel

- Jellyfin Server 10.11.11 and API packages 10.11.11;
- Linux x64/arm64, Windows x64, macOS arm64 where Jellyfin supports it;
- Jellyfin Web plus one Apple and one television client;
- Channel listing, direct play, transcode fallback, cancellation, pagination, permissions, refresh;
- no FUSE and no open listener.

## Jellyfin Managed Library

- Linux FUSE and Windows WinFsp;
- mount start, health, server restart, unexpected exit, repair, disable, uninstall;
- library registration and refresh;
- Infuse connection and playback.

## Emby

- Emby Server 4.9.3.0 and 4.10.0.11-beta;
- Channel and Managed Library behaviors equivalent to Jellyfin;
- Emby Web plus one Apple and one television client;
- Catalog package validation.

## Security

- runtime checksum mismatch;
- archive extraction traversal;
- command and argument injection;
- credential and signed-URL redaction;
- read-only mount enforcement;
- cancellation and process-tree termination;
- no RC TCP listener.

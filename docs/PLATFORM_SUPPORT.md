# Platform Support

| Host | Primary integration | Optional integration | Separate ArchiveMediaDrive service | Status |
|---|---|---|---|---|
| Kodi | Python plugin source | None required | No | Implemented |
| Jellyfin | Native Channel plugin | Plugin-managed rclone mount + standard library | No | Implemented |
| Emby | Native Channel plugin | Plugin-managed rclone mount + standard library | No | Implemented |
| Infuse | Through Jellyfin/Emby standard library; Channel behavior must be tested | — | No additional service | UAT pending |
| Plex | No supported provider API | Generic filesystem mount only | Would require non-native integration | Unsupported |

## Kodi constraints

- Official Python add-ons must contain source code and cannot include `.so`, `.dll`, `.exe`, or other compiled binaries.
- ArchiveMediaDrive uses the official `internetarchive` Python package and Kodi's player.
- The add-on is eligible for a third-party repository immediately; official repository submission requires source-vendoring and content-policy review.

## Jellyfin constraints

- Channels are the supported remote audio/video content extension point.
- Channel mode works without a mount.
- Managed Library mode requires operating-system mount support and should be enabled only after a prerequisite check.
- Infuse may expose standard Jellyfin libraries more consistently than Channels, so both modes require real-client verification.

## Emby constraints

- Server plugins and Channel plugins are supported and have official templates.
- Managed Library mode shares the Jellyfin runtime model but uses Emby-specific library APIs.
- Catalog publication follows beta testing and Emby's submission process.

## Plex constraint

Plex has removed plugin-based content playback and continues to retire plug-in support. ArchiveMediaDrive cannot provide a supported Google Drive-like provider inside current Plex clients. The project will not ship a legacy Channel that creates a false compatibility promise.

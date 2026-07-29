# Plex status

ArchiveMediaDrive does not ship a Plex provider.

Plex removed plugin-based content playback and is retiring its legacy plug-in framework. A `.bundle` implementation would require manual installation, would not appear as a supported provider in modern clients, and could stop working without a compatible replacement API.

Revisit this directory only when Plex publishes a supported third-party content-source SDK. A generic rclone filesystem mount is a separate compatibility method and does not satisfy the plugin-native product requirement.

## Release checklist for revisiting Plex support

Before adding a Plex adapter, verify all of the following:

1. Plex publishes a supported, documented third-party content-source SDK.
2. The SDK allows custom content providers to appear in the standard Plex client navigation.
3. The SDK supports direct HTTP playback with Range requests for remote URLs.
4. The SDK is compatible with current Plex server and client versions.
5. The SDK license does not conflict with AGPL-3.0-or-later.

If any item is unmet, do not start implementation. Document the gap and revisit at the next Plex release cycle.


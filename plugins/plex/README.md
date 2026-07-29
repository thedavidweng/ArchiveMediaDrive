# Plex status

ArchiveMediaDrive does not ship a Plex provider.

Plex removed plugin-based content playback and is retiring its legacy plug-in framework. A `.bundle` implementation would require manual installation, would not appear as a supported provider in modern clients, and could stop working without a compatible replacement API.

Revisit this directory only when Plex publishes a supported third-party content-source SDK. A generic rclone filesystem mount is a separate compatibility method and does not satisfy the plugin-native product requirement.

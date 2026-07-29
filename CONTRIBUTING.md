# Contributing

ArchiveMediaDrive is plugin-first. Changes must preserve these boundaries:

- no standalone end-user service;
- no media ranking, subtitle matching, metadata scraping, or transcoding;
- Kodi remains source-only and uses the official Internet Archive Python library;
- Jellyfin/Emby reuse a pinned official rclone runtime;
- Plex remains unsupported under the retired plugin framework;
- general rclone or Internet Archive defects should be proposed upstream.

Run contract tests, adapter tests, package-policy checks, and the relevant host integration suite before opening a pull request.

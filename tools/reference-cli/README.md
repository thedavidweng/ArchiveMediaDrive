# ArchiveMediaDrive reference CLI

This command-line implementation is retained for fixture generation, contract tests, and architecture experiments. It is not the supported end-user deployment. Production users install a Kodi, Jellyfin, or Emby adapter.

The mounted library view (`webdav`, `mount`, `run`) serves at most 200 resolved items. `sync` fails with an error above the limit. See `docs/adr/0006-mounted-library-catalog-envelope.md`. Use a channel mode adapter for large collections.


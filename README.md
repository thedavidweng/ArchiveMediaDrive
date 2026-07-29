# ArchiveMediaDrive

**Use Internet Archive as a native media source inside Kodi, Jellyfin, and Emby.**

ArchiveMediaDrive turns Internet Archive items, Collections, Favorites, and saved searches into provider-style folders inside media applications. Users install the adapter for the media server or player they already run, add an Internet Archive source, and browse the result from that application's normal interface.

ArchiveMediaDrive has no required standalone server, public port, WebDAV deployment, reverse proxy, or separate service account.

## What the project does

A configured source becomes a stable tree:

```text
Internet Archive
├── My Favorites
│   ├── item-identifier-a
│   │   └── files exposed by the Internet Archive item
│   └── item-identifier-b
│       └── files exposed by the Internet Archive item
└── Prelinger Collection
    └── item-identifier-c
        └── files exposed by the Internet Archive item
```

ArchiveMediaDrive preserves Internet Archive identifiers, filenames, directories, and available files. The host application remains responsible for playback, format support, subtitle handling, metadata presentation, transcoding, and watch state.

## Native integrations

### Kodi

`plugin.video.archivemediadrive` is a pure Python video/audio source. It appears inside Kodi's normal add-on browser, presents Internet Archive sources as folders, and resolves selected files to Archive.org URLs for Kodi's player.

The Kodi adapter uses the official `internetarchive` Python library. It does not bundle rclone because Kodi's official Python add-on repository rejects compiled binaries. A release build vendors the source form of the required Python packages.

### Jellyfin

The Jellyfin server plugin provides two modes:

- **Channel mode:** the default. Sources appear as a native remote-content channel. The plugin lists item files and returns direct media URLs. It requires no FUSE mount and no persistent helper process.
- **Managed Library mode:** optional. The plugin downloads a pinned official rclone build, verifies its checksum, starts `rclone mount` as a child of Jellyfin, registers the mounted root as a normal library path, and stops the mount with the server. This mode is intended for standard-library behavior and clients such as Infuse that may not expose Jellyfin Channels.

### Emby

The Emby server plugin follows the same model:

- native Channel mode for zero-setup browsing;
- optional plugin-managed rclone mount for a standard Emby library.

The user installs one plugin and configures sources in the Emby dashboard. ArchiveMediaDrive does not run as an independently deployed service.

### Plex

Modern Plex does not provide a supported content-provider plugin path. Plex removed plugin-based content playback and continues to retire the legacy plug-in framework. ArchiveMediaDrive therefore ships no Plex provider. Generic filesystem mounting remains outside the Plex plugin deliverable because it cannot offer the requested native installation model.

## Installation model

The repository currently contains scaffolds; published packages will install through each host's native extension workflow.

### Kodi

1. Install the ArchiveMediaDrive repository ZIP.
2. Open **Add-ons → Install from repository → ArchiveMediaDrive → Video add-ons**.
3. Install **ArchiveMediaDrive**.
4. Open the add-on settings and add an item, Collection, Favorites user, or search source.

Development packaging:

```bash
python plugins/kodi/build_vendor.py
# Package plugin.video.archivemediadrive as a Kodi add-on ZIP.
```

The implementation agent must complete the deterministic vendor script before a user-facing package is published.

### Jellyfin

1. Add the ArchiveMediaDrive plugin-repository manifest in the Jellyfin dashboard.
2. Install **ArchiveMediaDrive** from the Catalog and restart Jellyfin.
3. Configure sources under the plugin page.
4. Use Channel mode by default. Enable Managed Library mode only when a standard Jellyfin library is required.

The plugin downloads and verifies its pinned official rclone runtime automatically. Managed Library mode performs prerequisite checks for FUSE or WinFsp.

### Emby

1. Install the ArchiveMediaDrive beta package or Catalog entry.
2. Restart Emby and open the plugin configuration page.
3. Add sources and use Channel mode.
4. Enable Managed Library mode only for standard-library clients.

### Development checks

```bash
make verify-tree
make test
```

The server adapters use .NET SDK 9.0. Jellyfin is pinned to Server/API 10.11.11; Emby compiles against `MediaBrowser.Server.Core` 4.9.1.90 and is tested against Server 4.9.3.0 plus 4.10.0.11-beta.

## Source types

Each adapter supports the same source contract:

```json
{
  "id": "my-favorites",
  "name": "My Favorites",
  "kind": "favorites",
  "value": "archive-org-username",
  "refreshMinutes": 360
}
```

Supported `kind` values:

- `item`: one item URL or identifier;
- `collection`: one Collection URL or identifier;
- `favorites`: one public Archive.org username or `fav-*` identifier;
- `search`: one Internet Archive search expression.

## What ArchiveMediaDrive deliberately does not do

- choose a preferred MKV, MP4, audio file, subtitle, or derivative;
- rename files or reorganize seasons and episodes;
- scrape TMDb, MusicBrainz, or other metadata providers;
- download or match subtitles;
- transcode, remux, or proxy media through an ArchiveMediaDrive server;
- maintain a separate user-facing media database;
- replace the host application's playback or watch-state systems.

Adapters may declare a file's basic audio/video type when a host API requires it. That declaration uses the Internet Archive file metadata and filename only; it is not a preferred-file selection layer.

## Upstream foundations

ArchiveMediaDrive builds on existing projects:

- the official Internet Archive `internetarchive` Python library and `ia` CLI for source and item behavior;
- rclone's Internet Archive backend for item file access, Range reads, and public links;
- rclone mount for optional standard-library projection;
- Kodi's Python plugin-source API;
- Jellyfin and Emby Channel and server-plugin APIs.

The .NET adapters use the public Internet Archive search API for source resolution because Jellyfin and Emby do not ship a Python runtime. Their behavior is contract-tested against fixtures produced by the official `ia` client.

## Repository layout

```text
plugins/
├── kodi/       pure Python Kodi provider
├── jellyfin/   Jellyfin Channel + managed-library plugin
├── emby/       Emby Channel + managed-library plugin
└── plex/       documented platform limitation

shared/
├── dotnet/     source resolution and rclone orchestration shared by server plugins
└── contracts/  source and raw-node contracts

runtime/
└── rclone/     pinned runtime manifest and bootstrap rules

tools/
└── reference-cli/  development harness; not an end-user deployment
```

## Current status

This archive is a repository scaffold plus an implementation-grade specification. It includes:

- the shared contracts;
- Kodi, Jellyfin, and Emby project skeletons;
- a concrete rclone runtime policy;
- source-resolution and raw-tree boundaries;
- packaging and release plans;
- acceptance tests and platform-specific constraints;
- the previous command-line prototype retained only as a reference harness.

The next coding agent should implement the workstreams in `AGENT_IMPLEMENTATION_PLAN.md` without changing the architectural decisions in `SPEC.md`.

## Security model

- rclone Remote Control is never exposed over a TCP listener in normal operation.
- Channel mode invokes `rclone rc --loopback` as a short-lived subprocess.
- Managed Library mode starts one plugin-owned `rclone mount` child process.
- rclone downloads are pinned and SHA-256 verified.
- source refresh is read-only and media bytes flow directly from Archive.org to the host player or rclone mount.
- credentials remain in the host plugin's private configuration directory.

## Distribution targets

- Kodi: ArchiveMediaDrive add-on repository first, then submission to Kodi's official repository after source-only packaging and policy review.
- Jellyfin: ArchiveMediaDrive plugin repository manifest plus GitHub Release ZIPs.
- Emby: beta DLL/ZIP releases followed by Emby Catalog submission.
- Plex: no provider package until Plex exposes a supported content-source extension API.

## License

ArchiveMediaDrive is licensed under AGPL-3.0-or-later. rclone is MIT-licensed. The Internet Archive Python client is AGPL-3.0.

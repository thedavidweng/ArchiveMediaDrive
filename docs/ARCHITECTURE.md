# Architecture

```text
                           Internet Archive
                    search API / item metadata / files
                                  |
             +--------------------+--------------------+
             |                                         |
             v                                         v
     Kodi Python add-on                       Jellyfin / Emby plugin
  official internetarchive lib                 shared .NET control core
             |                                         |
             |                               source identifier snapshots
             |                                         |
             |                                  rclone rc --loopback
             |                              list item files / public links
             |                                         |
             v                                         v
      Kodi folder UI                          native Channel folder UI
      Kodi HTTP player                       host player / transcoder
                                                       |
                                           optional Managed Library mode
                                                       |
                                       plugin-owned rclone mount child
                                                       |
                                           standard host library path
```

## Runtime boundary

ArchiveMediaDrive is installed into the host application. It has no required standalone process or network service.

Kodi uses the official Internet Archive Python package and hands selected file URLs directly to Kodi.

Jellyfin and Emby call the official rclone executable as an implementation detail. Channel mode uses short-lived `rclone rc --loopback` subprocesses. Managed Library mode supervises one foreground `rclone mount` child tied to the host lifecycle.

## Ownership boundary

ArchiveMediaDrive owns:

- source declarations;
- source-to-identifier snapshots;
- provider folder placement;
- plugin settings and refresh status;
- safe rclone runtime bootstrap and invocation;
- mapping raw provider nodes into each host extension API.

Internet Archive and the official client own:

- search semantics;
- item identifiers and metadata;
- file metadata;
- authentication behavior.

rclone owns:

- Internet Archive item file listing in server adapters;
- file reads and byte ranges;
- public download links;
- optional filesystem mounting.

Kodi, Jellyfin, Emby, Infuse, and their playback engines own:

- codec support;
- media-file selection;
- subtitle behavior;
- playback UI;
- transcoding;
- metadata presentation;
- watch and resume state.

## Why rclone stays out of the Kodi package

Kodi's official Python add-on rules reject compiled binaries. Kodi already provides native remote URL playback and a Python runtime. The Kodi adapter therefore uses the official `internetarchive` Python package and direct Archive.org URLs.

## Why server plugins do not load librclone

rclone provides an experimental C library interface. It includes a Go runtime, has platform-specific mount build requirements, and cannot safely be unloaded on Windows. Loading it inside a long-running media server increases crash radius. A verified helper executable with loopback-only, short-lived calls preserves process isolation while remaining invisible to the user.

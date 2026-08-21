# Emby guide

ArchiveMediaDrive for Emby adds Internet Archive sources to your Emby server.

## Requirements

- Emby Server 4.9.3.0 or later.
- Channel mode: no extra software.
- Managed Library mode: FUSE on Linux, WinFsp on Windows, or macOS mount support.

## Install

1. Download the ArchiveMediaDrive beta package from the project releases, or install it from the Catalog when available.
2. Restart Emby.

On first use, the plugin downloads a pinned official rclone build, verifies its checksum, and stores it in the plugin data folder. You do not install rclone yourself.

## Configure

1. Open the Emby dashboard and go to **Plugins → ArchiveMediaDrive**.
2. Add your Sources in the Sources list. See the [source guide](sources.md) for fields and examples.
3. Keep **Channel enabled** selected for normal use.
4. Save the settings.

The plugin page has these options:

| Option | Meaning |
|---|---|
| Channel enabled | Show Sources through the Emby channel interface. On by default. |
| Managed Library enabled | Mount Internet Archive content as a read-only library through rclone. |
| Managed Library name | Display name of the managed library folder. Default: `Internet Archive`. |
| Sources | The Internet Archive Sources to browse and mount. |

## Channel mode

Channel mode is the default. Sources appear as a channel in Emby.

- Browse sources in the Channels area.
- Play items directly from Archive.org. No mount is used.
- Playback behavior follows Emby and your player.

## Managed Library mode

Managed Library mode shows Internet Archive content as a standard Emby library.

Before you enable it:

1. Install FUSE on Linux, or WinFsp on Windows. On macOS, confirm mount support is available.
2. Confirm your catalog is small enough. The mounted view serves at most 200 resolved items across all Sources. Larger catalogs are rejected at configuration time with an error that names the limit.

Then:

1. Select **Managed Library enabled**.
2. Set the library display name if needed.
3. Save the settings.

The plugin starts one read-only rclone mount as a child of Emby, registers the mount root as a library path, and stops the mount during shutdown or uninstall.

## Troubleshoot

| Problem | What to do |
|---|---|
| A Source shows no content | Check the kind and value against the [source guide](sources.md). Public access works without an account. |
| Runtime or rclone errors | Check server logs. The rclone download is checksum verified; failures are usually network related. |
| Managed Library will not start | Install FUSE or WinFsp first. Confirm mount permissions for the Emby service user. |
| Catalog rejected above 200 items | Reduce resolved items, or keep those Sources in Channel mode only. |
| Stale content | Wait for the refresh interval, or re-save the Source to refresh now. |

## Uninstall

Remove the plugin in the dashboard and restart Emby. The managed mount stops with the uninstall. Media on Archive.org is not touched.

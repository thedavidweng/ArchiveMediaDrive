# Jellyfin guide

ArchiveMediaDrive for Jellyfin adds Internet Archive sources to your Jellyfin server.

## Requirements

- Jellyfin Server 10.11.11 or compatible.
- Channel mode: no extra software.
- Managed Library mode: FUSE on Linux, WinFsp on Windows, or macOS mount support. Containers need matching privileges.

## Install

1. Open the Jellyfin dashboard.
2. Go to **Plugins → Repositories** and add the ArchiveMediaDrive repository manifest.
3. Go to **Plugins → Catalog**, find **ArchiveMediaDrive**, and install it.
4. Restart Jellyfin.

On first use, the plugin downloads a pinned official rclone build, verifies its checksum, and stores it in the plugin data folder. You do not install rclone yourself.

## Configure

1. Open the dashboard and go to **Plugins → ArchiveMediaDrive**.
2. Enter your Sources as a JSON array in the **Sources JSON** field. See the [source guide](sources.md) for the format.
3. Keep **Channel mode enabled** selected for normal use.
4. Select **Save**.

Example Sources JSON:

```json
[
  {
    "id": "prelinger",
    "name": "Prelinger Archives",
    "kind": "collection",
    "value": "prelinger",
    "enabled": true,
    "refreshMinutes": 720
  }
]
```

## Channel mode

Channel mode is the default. Sources appear as a remote-content channel in Jellyfin.

- Browse sources in the Channels area of the web app and compatible clients.
- Play items directly from Archive.org. No mount and no extra storage are used.
- Seek and transcode behavior follows Jellyfin and your player.

## Managed Library mode

Managed Library mode shows Internet Archive content as a standard Jellyfin library. Use it when a client does not expose Channels well, for example Infuse.

Before you enable it:

1. Install FUSE on Linux, or WinFsp on Windows. On macOS, confirm mount support is available.
2. Confirm your catalog is small enough. The mounted view serves at most 200 resolved items across all Sources. Larger catalogs are rejected at save time with an error that names the limit.

Then:

1. Select **Managed Library mode enabled**.
2. Set the library display name. The default is `Internet Archive`.
3. Select **Save**.

The plugin starts one read-only rclone mount as a child of Jellyfin, registers the mount root as a library path, and refreshes the library after Source changes. The mount stops when the server stops or when you disable the mode.

## Check status

The plugin page shows live status fields:

- runtime and rclone version;
- mount status and mount path;
- cache usage;
- last refresh time;
- source count and item count;
- last error.

Select **Download diagnostics** to get a diagnostics file for problem reports.

## Troubleshoot

| Problem | What to do |
|---|---|
| Sources do not appear | Check the JSON syntax. Each entry needs valid `id`, `name`, `kind`, `value`, `enabled`, and `refreshMinutes`. |
| Runtime status shows an error | Check server logs. The plugin verifies the rclone checksum; a failed download means a network or proxy problem. |
| Managed Library will not start | Install FUSE or WinFsp first. In containers, add the required device mappings and privileges. |
| Catalog rejected above 200 items | Reduce resolved items or switch those Sources back to Channel mode only. |
| Stale content | Wait for the refresh interval, or edit and re-save the Sources JSON to trigger a refresh. |

## Uninstall

Remove the plugin in **Plugins** and restart Jellyfin. The managed mount stops with the uninstall. Media on Archive.org is not touched.

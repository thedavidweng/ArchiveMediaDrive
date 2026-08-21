# Kodi guide

ArchiveMediaDrive for Kodi adds Internet Archive items, Collections, Favorites, and searches as Kodi folders.

## Requirements

- Kodi 21 (Omega) or later.
- No other software. The add-on uses the official Internet Archive Python library. It does not install rclone.

## Install

1. Download the ArchiveMediaDrive repository ZIP from the project releases.
2. In Kodi, open **Add-ons → Install from zip file** and select the repository ZIP.
3. Open **Add-ons → Install from repository → ArchiveMediaDrive → Video add-ons**.
4. Select **ArchiveMediaDrive** and install it.

## Add a Source

Sources are managed inside the add-on. You do not edit settings files.

1. Open **Video add-ons → ArchiveMediaDrive**.
2. Select **Manage sources**.
3. Select **Add source**.
4. Enter a display name, for example `Prelinger Archives`.
5. Accept or change the suggested Source ID.
6. Select the kind: `item`, `collection`, `favorites`, or `search`.
7. Enter the value:
   - item: an Archive.org details URL or identifier;
   - collection: a Collection URL or identifier;
   - favorites: a public Archive.org username;
   - search: an Internet Archive search expression.
8. Accept the refresh interval, or enter your own in minutes. The default is 360.

The new Source appears on the add-on start page.

## Manage Sources

Select **Manage sources**, then select a Source:

- **Edit**: change the name, kind, value, or refresh interval.
- **Delete**: remove the Source.
- **Test**: check the Source against Archive.org and show a count.
- **Refresh**: resolve the Source again now.

## Browse and play

- Select a Source to see its items.
- Select an item to see its files and folders. Original filenames and subdirectories stay unchanged.
- Select a file to play it. Kodi streams the bytes from Archive.org. Nothing is downloaded in advance.
- Seek, pause, resume, and subtitles behave like any other Kodi source.

Files that Kodi cannot play stay visible in the list. ArchiveMediaDrive does not hide files or pick one preferred version for you.

## Troubleshoot

| Problem | What to do |
|---|---|
| A Source shows no items | Use **Manage sources → Test**. Check that the identifier is correct and the item is public. |
| Playback fails | Check your internet connection. Large files need stable bandwidth. |
| Private items are missing | Private items need credentials. Public access is the default. |
| Stale content | Use **Manage sources → Refresh** on the affected Source. |

## Uninstall

Open **Add-ons → My add-ons → Video add-ons → ArchiveMediaDrive** and select **Uninstall**. This removes the add-on data from the Kodi profile. Media on Archive.org is not touched.

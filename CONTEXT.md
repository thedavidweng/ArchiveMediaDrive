# ArchiveMediaDrive

ArchiveMediaDrive puts Internet Archive content into Kodi, Jellyfin, and Emby. It is a set of host plugins. It is not a standalone service.

## Language

**Source**:
An Internet Archive origin the user adds to ArchiveMediaDrive. It is an item, a Collection, Favorites, or a search. It becomes one folder tree in the host application.
_Avoid_: feed, channel, library, provider

**Item**:
One Internet Archive identifier. Its Source makes one item folder.
_Avoid_: single item, one-off

**Collection**:
An Internet Archive Collection. Its Source makes the Collection item list.
_Avoid_: set, group, folder

**Favorites**:
The public favorites of an Internet Archive user. Its Source makes the starred item list.
_Avoid_: stars, bookmarks, liked

**Search**:
An Internet Archive search expression. Its Source makes the result set.
_Avoid_: query, filter, saved search

**Identifier**:
The unique ID of an Internet Archive item, set by Archive.org. It is stable and unique across Archive.org.
_Avoid_: item id, slug, key

## Source lifecycle

**Resolve**:
The action to change a Source into its Results. The Adapter or the Add-on does it with the Internet Archive API.
_Avoid_: fetch, query, lookup

**Results**:
The ordered list of item identifiers for one Source at one refresh time. A new Results replaces the old one only after full success.
_Avoid_: snapshot, cache, result list

**Refresh**:
The action to resolve a Source again and replace its Results. The host scheduler or a manual command starts it. A failed Refresh keeps the old Results.
_Avoid_: sync, update, poll

## Provider tree

**RawNode**:
One node in the provider folder tree. It is a root, an item, a directory, or a file.
_Avoid_: entry, node, element

**Root node**:
The top RawNode of a Source tree. Its children are item nodes.
_Avoid_: source node, top node

**Item node**:
A RawNode for one Internet Archive item. Its children are directory and file nodes.
_Avoid_: identifier node

**Directory node**:
A RawNode for a subdirectory in an item. It keeps the original Internet Archive path.
_Avoid_: folder node

**File node**:
A RawNode for one file in an item. It has the identifier, the path, the size, the format, and the public link.
_Avoid_: leaf, playable

## File metadata

**File source**:
The `source` field of an Internet Archive file. The value is `original`, `derivative`, or other. The File node stores it as `iaSource`.
_Avoid_: source (bare), origin

**Derivative**:
An Internet Archive file made from an original file. The `source` field of the file is `derivative`. The Adapter does not select one Derivative over another.
_Avoid_: transcoded file, generated file, alternate

**Public link**:
The URL to download one file from Archive.org. The rclone `operations/publiclink` command makes it. The File node stores it as `publicUrl`.
_Avoid_: public URL, download URL, direct link

## Host integration

**Host**:
The media application where ArchiveMediaDrive runs. It is Kodi, Jellyfin, Emby, or Infuse.
_Avoid_: server, player, client

**Adapter**:
The code package that puts RawNodes into one host application. It is the general term for all host packages.
_Avoid_: bridge, connector, integration

**Add-on**:
The Kodi adapter. It is a Python plugin source packaged as a Kodi add-on.
_Avoid_: plugin (for Kodi)

**Plugin**:
The Jellyfin or Emby adapter. It is a .NET server plugin installed through the host plugin system.
_Avoid_: add-on (for Jellyfin or Emby)

**Channel mode**:
The default Plugin mode. Sources show as host channels. No mount is used.
_Avoid_: channel (bare), remote mode

**Managed Library mode**:
The optional Plugin mode. The Plugin starts an rclone mount and adds the mount as a standard host library.
_Avoid_: library mode, mount mode

## rclone

**rclone runtime**:
The rclone executable that a Plugin downloads, verifies, and manages. It is pinned to one version set by an ArchiveMediaDrive release.
_Avoid_: rclone binary, rclone instance, rclone daemon

**loopback**:
The rclone `--loopback` flag. It lets the Plugin call rclone RC commands through an in-process pipe, not a TCP socket. No network listener is open.
_Avoid_: local mode, in-process, pipe mode

**Mount**:
The rclone `mount` command. Managed Library mode uses it to show the Internet Archive as a local filesystem.
_Avoid_: fuse mount, virtual drive, filesystem

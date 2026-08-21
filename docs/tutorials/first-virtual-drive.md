# Tutorial: your first virtual drive

In this tutorial you turn one Internet Archive item into a browsable folder tree and look inside it. You use the reference CLI from this repository. The same source-to-tree shape is what the Kodi add-on and the Jellyfin and Emby plugins build inside their host applications.

The reference CLI is the development harness for ArchiveMediaDrive. It is not the supported end-user deployment. Production users install a Kodi, Jellyfin, or Emby adapter. This tutorial is for evaluating the project and for contributors who want to see the provider tree before touching a media server.

Time: about 10 minutes.

## Before you start

You need:

- Python 3.11 or later;
- the official Internet Archive client (`ia`) on your `PATH`;
- rclone on your `PATH`;
- a checkout of this repository.

Check the two external tools with the reference CLI:

```console
$ cd tools/reference-cli
$ python3 -m archivemediadrive doctor
```

```text
OK ia: 5.9.0
OK rclone: rclone v1.71.1
```

Every command in this tutorial is `python3 -m archivemediadrive`. Run it from `tools/reference-cli`, or from any directory with:

```bash
PYTHONPATH=/path/to/ArchiveMediaDrive/tools/reference-cli \
  python3 -m archivemediadrive <command>
```

If either `doctor` line starts with `FAIL`, install the [official `ia` client](https://archive.org/developers/ia.html) or [rclone](https://rclone.org/install/) first.

## 1. Create a Source configuration

Work in a scratch directory outside the repository so state files stay out of your checkout:

```bash
mkdir -p ~/amd-demo && cd ~/amd-demo
```

Create `config.toml` with one Source of kind `item`:

```toml
version = 1

[serve]
address = "127.0.0.1:8080"
remote_name = "archive-media-drive"

[[sources]]
name = "A Trip Down Market Street"
kind = "item"
value = "TripDown1905"
```

`TripDown1905` is a public Internet Archive item: a 1905 film shot from the front of a streetcar on Market Street, San Francisco. Every command below was run against this configuration.

## 2. Resolve the Source

Resolve turns the Source into its item list and renders the rclone configuration:

```console
$ python3 -m archivemediadrive --config config.toml sync
```

```text
synchronized 1 sources and 1 virtual item directories
```

Two files now exist under `.amd/` in the working directory:

```text
.amd/catalog.json   the resolved catalog: which items belong to which Source
.amd/rclone.conf    the rclone remotes that project the catalog as folders
```

## 3. Inspect the catalog

```console
$ python3 -m archivemediadrive --config config.toml catalog
```

```json
{
  "schemaVersion": 1,
  "generatedAt": "2026-08-21T21:39:45.727016+00:00",
  "sources": [
    {
      "name": "A Trip Down Market Street",
      "kind": "item",
      "value": "TripDown1905",
      "path": "A Trip Down Market Street",
      "identifiers": [
        "TripDown1905"
      ]
    }
  ]
}
```

One Source resolved to one identifier. `generatedAt` shows when this Results was written; a later `sync` replaces the whole file.

## 4. Browse the tree

Serve the catalog over WebDAV on the address from `[serve]`:

```console
$ python3 -m archivemediadrive --config config.toml webdav
```

The server announces itself and keeps running:

```text
2026/08/21 14:55:43 NOTICE: combine root '': WebDav Server started on [http://127.0.0.1:8080/]
```

Leave it running and list the root with a second terminal. The root holds one folder per Source:

```console
$ curl -s -X PROPFIND -H 'Depth: 1' http://127.0.0.1:8080/ | grep -o '<D:href>[^<]*</D:href>'
```

```text
<D:href>/</D:href>
<D:href>/A%20Trip%20Down%20Market%20Street/</D:href>
```

Inside the Source folder, each resolved identifier gets one folder. List the item folder to see its files:

```console
$ curl -s -X PROPFIND -H 'Depth: 1' \
  'http://127.0.0.1:8080/A%20Trip%20Down%20Market%20Street/TripDown1905/' \
  | grep -o '<D:href>[^<]*</D:href>' | sed 's/<[^>]*>//g'
```

```text
/A%20Trip%20Down%20Market%20Street/TripDown1905/
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.asr.js
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.asr.srt
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.gif
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.mp3
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.mp4
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.mpeg
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.mpeg.idx
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.ogv
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.png
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.storj-store.log
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.storj-store.trigger
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905.thumbs/
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905_256kb.rm
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905_512kb.mp4
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905_64kb.rm
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905_archive.torrent
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905_files.xml
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905_meta.sqlite
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905_meta.xml
/A%20Trip%20Down%20Market%20Street/TripDown1905/TripDown1905_reviews.xml
/A%20Trip%20Down%20Market%20Street/TripDown1905/__ia_thumb.jpg
```

This is the ArchiveMediaDrive contract in one view: original Internet Archive filenames, every derivative next to the original, and the item's `TripDown1905.thumbs/` subdirectory preserved. Nothing is renamed, filtered, or chosen for you. Stop the server with `Ctrl-C`.

## 5. See the size envelope

The WebDAV and mount views serve at most 200 resolved items across all Sources. Two modest search Sources cross that line. Save this as `config-over-limit.toml`:

```toml
version = 1

[serve]
address = "127.0.0.1:8080"
remote_name = "archive-media-drive"

[[sources]]
name = "Prelinger automobiles"
kind = "search"
value = "collection:prelinger AND subject:automobiles"

[[sources]]
name = "Prelinger 1930"
kind = "search"
value = "collection:prelinger AND year:1930"
```

```console
$ python3 -m archivemediadrive --config config-over-limit.toml sync
```

```text
error: the resolved catalog has 278 items; the mounted library supports at most 200 items; use a channel mode adapter for larger catalogs
```

The command exits with status 1 and the previous catalog is left in place. Channel mode has no such limit. See [ADR 0006](../adr/0006-mounted-library-catalog-envelope.md) for why the bound exists.

## If something goes wrong

| Symptom | Cause and fix |
|---|---|
| `FAIL` from `doctor` | `ia` or `rclone` is missing from `PATH`. Install it and run `doctor` again. |
| `error: [Errno 2] No such file or directory: '.amd/catalog.json'` | You ran `catalog` before the first `sync`. Run `sync` first. |
| A Source resolves to zero items | Check the identifier or search expression against [the Source reference](../reference/sources.md). |
| `ia search` times out | Large Collections can take minutes on the Archive.org search API. Try a narrower search expression. |

## Where next

- Pick your host: [Kodi](../how-to/kodi.md), [Jellyfin](../how-to/jellyfin.md), or [Emby](../how-to/emby.md).
- All Source kinds and fields: [Source reference](../reference/sources.md).
- Every reference CLI command: [reference CLI reference](../reference/reference-cli.md).
- How the pieces fit together: [Architecture](../ARCHITECTURE.md).

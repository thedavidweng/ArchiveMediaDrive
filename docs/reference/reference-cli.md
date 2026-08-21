# Reference CLI (`amd`)

The reference CLI is the Python development harness in `tools/reference-cli`. It exposes Internet Archive Sources as an rclone-backed virtual drive. It exists for fixture generation, contract tests, and architecture experiments. Production users install a Kodi, Jellyfin, or Emby adapter; this CLI is not the supported end-user deployment.

The mounted views (`webdav`, `mount`, `run`) serve at most 200 resolved items. See [ADR 0006](../adr/0006-mounted-library-catalog-envelope.md).

## Synopsis

```text
amd [--config CONFIG] [--state-dir STATE_DIR]
    [--ia-binary IA_BINARY] [--rclone-binary RCLONE_BINARY]
    COMMAND
```

Run it without installing from a repository checkout:

```bash
cd tools/reference-cli
python3 -m archivemediadrive --help
```

Or install the package to get the `amd` entry point:

```bash
pip install tools/reference-cli
amd --help
```

Top-level help, as printed by `python3 -m archivemediadrive --help`:

```text
usage: amd [-h] [--config CONFIG] [--state-dir STATE_DIR]
           [--ia-binary IA_BINARY] [--rclone-binary RCLONE_BINARY]
           {doctor,sync,catalog,fixtures,webdav,mount,run} ...

Expose Internet Archive sources as an rclone-backed virtual drive.

positional arguments:
  {doctor,sync,catalog,fixtures,webdav,mount,run}
    doctor              check required external tools
    sync                resolve configured sources and render rclone config
    catalog             print the last synchronized catalog
    fixtures            capture canonical Internet Archive fixtures using the
                        official ia client
    webdav              serve the virtual drive over WebDAV
    mount               mount the virtual drive as a local filesystem
    run                 synchronize, then serve WebDAV

options:
  -h, --help            show this help message and exit
  --config CONFIG
  --state-dir STATE_DIR
  --ia-binary IA_BINARY
  --rclone-binary RCLONE_BINARY
```

## Global options

| Option | Default | Meaning |
|---|---|---|
| `--config PATH` | `config.toml` | Source configuration file. See [the config file format](#config-file). |
| `--state-dir PATH` | `.amd` | Directory for generated state. Created on `sync`. |
| `--ia-binary NAME` | `ia` | Internet Archive client used for resolution and fixtures. |
| `--rclone-binary NAME` | `rclone` | rclone executable used for serving and mounting. |

Both external tools must be on `PATH`. Check them with `doctor`.

## Commands

### `doctor`

Checks that the required external tools are present and prints one line per tool:

```console
$ python3 -m archivemediadrive doctor
```

```text
OK ia: 5.9.0
OK rclone: rclone v1.71.1
```

Exit status is `0` only when every check passes.

### `sync`

Resolves every Source in the configuration through the official `ia` client, writes the state files, and prints a summary:

```console
$ python3 -m archivemediadrive --config config.toml sync
```

```text
synchronized 1 sources and 1 virtual item directories
```

A failed resolution aborts the whole command: no partial catalog is written, so an existing Results stays intact.

Above the mounted-library envelope the command fails with an error that names the limit:

```text
error: the resolved catalog has 278 items; the mounted library supports at most 200 items; use a channel mode adapter for larger catalogs
```

### `catalog`

Prints the last synchronized catalog as JSON:

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

Without a prior `sync` it fails:

```text
error: [Errno 2] No such file or directory: '.amd/catalog.json'
```

### `fixtures`

Recaptures the canonical Internet Archive fixtures that contract tests compare against. Writes into the directory given by `--out` (default: `contracts/fixtures/ia`) using `--manifest` (default: `contracts/fixtures/ia/manifest.json`). Run against live Archive.org; commit results only after review:

```console
$ python3 -m archivemediadrive fixtures
```

```text
OK               item-mkv-unicode
OK               item-mp4-derivatives-audio-subtitles-nested
OK               collection-prelinger-capped
OK               favorites-jake
OK               search-movies-prelinger-capped
EXPECTED-FAILURE unavailable-item
```

`OK` rows were recaptured successfully. `EXPECTED-FAILURE` marks a fixture whose failure is part of the contract (an item that is not publicly available).

### `webdav`

Serves the synchronized drive over WebDAV at `[serve].address` (default `127.0.0.1:8080`). Requires a prior `sync`:

```console
$ python3 -m archivemediadrive --config config.toml webdav
2026/08/21 14:55:43 NOTICE: combine root '': WebDav Server started on [http://127.0.0.1:8080/]
```

Options: `--address HOST:PORT` overrides the listen address; `--allow-public` permits unauthenticated non-loopback binds (refused by default); `--rclone-arg VALUE` appends one raw argument to the rclone invocation.

The server binds loopback only unless `--allow-public` is given.

### `mount`

Mounts the synchronized drive as a filesystem at the given mountpoint. Requires platform FUSE support (macFUSE on macOS, FUSE on Linux, WinFsp on Windows) and a prior `sync`.

### `run`

One step: `sync`, then `webdav`, with the same options.

## State directory

`sync` writes two files into `--state-dir` (default `.amd/`):

| File | Contents |
|---|---|
| `catalog.json` | The resolved catalog: `schemaVersion`, `generatedAt`, and per-Source `name`, `kind`, `value`, `path`, `identifiers`. |
| `rclone.conf` | Generated rclone remotes: one `internetarchive` remote for the data plane, one `combine` remote per Source path, and a top-level `combine` remote named after `[serve].remote_name`. |

For the tutorial Source above the generated configuration contains three remotes:

```ini
[archive-media-drive-ia]
type = internetarchive
description = Internet Archive data plane managed by ArchiveMediaDrive

[archive-media-drive-src-0]
type = combine
upstreams = "TripDown1905=archive-media-drive-ia:TripDown1905"
[archive-media-drive]
type = combine
upstreams = "A Trip Down Market Street=archive-media-drive-src-0:"
description = ArchiveMediaDrive virtual library
```

Each item becomes one combine upstream, which preserves the item's complete original file tree. Both files are derived state: delete the whole `.amd` directory at any time and `sync` rebuilds it.

## Config file

`--config` points at a TOML file. Full example with all four Source kinds:

```toml
version = 1

[serve]
address = "127.0.0.1:8080"       # webdav/run listen address
remote_name = "archive-media-drive"  # top-level rclone remote name

[[sources]]
name = "Example item"            # folder name under the root (unique)
kind = "item"                    # item | collection | favorites | search
value = "TripDown1905"

[[sources]]
name = "Prelinger"
kind = "collection"
value = "prelinger"
```

Rules enforced at load time:

- `version` must be `1`;
- at least one `[[sources]]` entry;
- each source needs `name`, `kind`, and `value`; `kind` is one of `item`, `collection`, `favorites`, `search`;
- optional `path` overrides the folder name derived from `name`; two sources cannot share one path;
- `address` must be a `host:port` string; `remote_name` must be alphanumeric plus `-` and `_`.

Value forms per kind follow [the Source reference](sources.md).

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. For `fixtures`: every fixture recaptured, or failed as expected. |
| `1` | Any failure: failed `doctor` check, resolution or envelope error, missing state, refused bind. |

Errors print one line to stderr prefixed with `error:`.

## Where next

- Walk the commands end to end: [Tutorial](../tutorials/first-virtual-drive.md).
- Validate a Source before adding it to a host: [Verify a Source](../how-to/verify-a-source.md).
- What the Kodi add-on and server plugins do with the same concepts: [Architecture](../ARCHITECTURE.md).

# How to verify a Source before adding it

Use this when you have an Identifier, Collection, Favorites owner, or search expression and want to confirm it resolves — and see how many items it holds — before typing it into Kodi, Jellyfin, or Emby.

The verification runs the same resolution path the reference CLI uses. Host adapters resolve through their own runtime, but the Archive.org side is identical.

## Quick count with the official client

If you only need a size estimate, ask Archive.org directly:

```console
$ ia search 'collection:prelinger AND year:1930' --itemlist | wc -l
```

```text
126
```

Counts drift as Archive.org changes, so treat the number as approximate. For search expressions this is also the fastest way to debug quoting: if `ia search` returns nothing, the expression needs fixing before anything else matters.

## Full resolution with the reference CLI

Prerequisites: Python 3.11+, the `ia` client, rclone, and a repository checkout. Check with [`doctor`](../reference/reference-cli.md#doctor) first.

Create a scratch directory outside the repository with your candidate Source in `config.toml`. This example uses one item:

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

Resolve it:

```console
$ python3 -m archivemediadrive --config config.toml sync
```

```text
synchronized 1 sources and 1 virtual item directories
```

Read back what resolved:

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

For a `search`, `collection`, or `favorites` Source the `identifiers` array holds every result — the 1930 query above resolves to the 126 entries that `wc -l` counted.

What to check:

| Result | Meaning |
|---|---|
| `synchronized 1 sources and N virtual item directories`, `identifiers` non-empty | The Source resolves. Enter it in your host with the same kind and value. |
| Zero identifiers | The identifier does not exist, the item is private, or the search expression matches nothing. |
| `error:` from `ia search failed` | Archive.org rejected the query — usually a syntax problem in a search expression. |

## Decide between Channel mode and Managed Library mode

Sum the identifier counts across all Sources you plan to enable. If the total stays at or below 200, both modes work. Above 200, Managed Library mode rejects the configuration when you save:

```text
error: the resolved catalog has 278 items; the mounted library supports at most 200 items; use a channel mode adapter for larger catalogs
```

In that case keep those Sources in Channel mode, or narrow the Sources until the total fits. See [ADR 0006](../adr/0006-mounted-library-catalog-envelope.md).

## Clean up

The `.amd` directory next to your config is derived state — catalog plus generated rclone remotes. Delete it freely; `sync` rebuilds it.

## Where next

- Field and kind rules: [Source reference](../reference/sources.md).
- Install the adapter: [Kodi](kodi.md), [Jellyfin](jellyfin.md), or [Emby](emby.md).

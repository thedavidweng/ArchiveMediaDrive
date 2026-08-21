# Source guide

A Source is one Internet Archive origin. It becomes one folder tree in your media application.

ArchiveMediaDrive supports four Source kinds:

| Kind | Value | Example value |
|---|---|---|
| `item` | One item URL or identifier | `https://archive.org/details/night_of_the_living_dead` or `night_of_the_living_dead` |
| `collection` | One Collection URL or identifier | `https://archive.org/details/prelinger` or `prelinger` |
| `favorites` | One public Archive.org username or `fav-*` identifier | `someuser` |
| `search` | One Internet Archive search expression | `collection:prelinger AND mediatype:(movies)` |

## Source fields

Every Source has these fields:

- `schemaVersion`: always `1`.
- `id`: a short name for internal use. Use lowercase letters, numbers, and hyphens. Maximum 64 characters.
- `name`: the display name in your application. Maximum 120 characters.
- `kind`: `item`, `collection`, `favorites`, or `search`.
- `value`: the URL or identifier for the kind.
- `enabled`: set to `false` to hide the Source without deleting it.
- `refreshMinutes`: how often the Source refreshes. Minimum 5, maximum 10080 (one week).
- `authenticationRef`: optional reference to stored credentials for private items.

## Examples

One item:

```json
{
  "id": "night-of-the-living-dead",
  "name": "Night of the Living Dead",
  "kind": "item",
  "value": "night_of_the_living_dead",
  "enabled": true,
  "refreshMinutes": 10080
}
```

One Collection:

```json
{
  "id": "prelinger",
  "name": "Prelinger Archives",
  "kind": "collection",
  "value": "prelinger",
  "enabled": true,
  "refreshMinutes": 720
}
```

One public Favorites list:

```json
{
  "id": "my-favorites",
  "name": "My Favorites",
  "kind": "favorites",
  "value": "archive-org-username",
  "enabled": true,
  "refreshMinutes": 360
}
```

One search:

```json
{
  "id": "silent-films",
  "name": "Silent Films",
  "kind": "search",
  "value": "mediatype:(movies) AND year:[1920-1929]",
  "enabled": true,
  "refreshMinutes": 1440
}
```

## How refresh works

A Refresh turns a Source into its item list. The host scheduler starts it at the interval you set. A manual Refresh command uses the same path.

A failed Refresh keeps the old item list. Your folders never go empty because of one bad request.

## Mounted library limit

The Managed Library mode of Jellyfin and Emby serves at most 200 resolved items across all Sources. Configuration above this limit is rejected with an error that names the limit.

Use Channel mode for large Collections. Channel mode has no such limit. See [ADR 0006](../adr/0006-mounted-library-catalog-envelope.md) for details.

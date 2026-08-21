# Source reference

A Source is one Internet Archive origin. Every adapter — the Kodi add-on and the Jellyfin and Emby plugins — turns each Source into one folder tree in its Host.

The normative definition is [`contracts/source.schema.json`](../../contracts/source.schema.json). Behavior is contract-tested against fixtures produced by the official `ia` client ([`contracts/fixtures/ia/`](../../contracts/fixtures/ia/)).

## Fields

| Field | Type | Required | Constraints | Meaning |
|---|---|---|---|---|
| `schemaVersion` | integer | stored form only | always `1` | Contract version. Hosts fill it in; you can omit it when entering a Source. |
| `id` | string | yes | lowercase letters, numbers, hyphens; starts with a letter or number; max 64 chars (`^[a-z0-9][a-z0-9-]{0,63}$`) | Stable internal name. |
| `name` | string | yes | 1–120 chars | Display name shown in your application. |
| `kind` | string | yes | `item`, `collection`, `favorites`, `search` | What kind of origin this Source points at. |
| `value` | string | yes | 1–4096 chars | The URL, identifier, username, or search expression for the kind. See [Kinds](#kinds). |
| `enabled` | boolean | yes | — | `false` hides the Source without deleting it. |
| `refreshMinutes` | integer | yes | 5–10080 (one week) | How often the Host resolves the Source again. |
| `authenticationRef` | string \| null | no | max 128 chars | Reference to stored credentials for private items. Public access needs none. |

## Kinds

| Kind | Value accepts | Resolves to |
|---|---|---|
| `item` | An Archive.org details URL (`https://archive.org/details/<identifier>`) or a bare Identifier | One Item folder |
| `collection` | A Collection details URL or a bare Identifier | The Collection's item list |
| `favorites` | A public Archive.org username, a `fav-<username>` identifier, or a matching details URL | That user's public starred item list |
| `search` | Any Internet Archive search expression, for example `mediatype:(movies) AND year:[1920-1929]` | The query's result set |

Normalization rules for all kinds:

- only `archive.org` and `www.archive.org` URLs are accepted;
- URLs must use the `/details/<identifier>` path; anything else is rejected;
- a bare value is treated as an Identifier;
- duplicate Items inside one Source are collapsed; result order from Archive.org is preserved;
- identifiers may contain letters, digits, dots, underscores, and hyphens.

## Examples

One Item:

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

One Search:

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

Hosts store `schemaVersion: 1` automatically; the examples omit it because the dashboard forms accept both.

## Resolution and Refresh

A Refresh resolves a Source into its ordered Results — the item list behind the folder tree. The Host scheduler starts it every `refreshMinutes`; editing and re-saving a Source triggers one immediately in every adapter.

A failed Refresh keeps the previous Results. Your folders never go empty because of one bad request or one Archive.org outage.

## Mounted library limit

Managed Library mode in Jellyfin and Emby serves at most **200 resolved Items across all Sources**. Configuration above the limit is rejected when you save, with an error that names the limit. The reference CLI words it:

```text
error: the resolved catalog has 278 items; the mounted library supports at most 200 items; use a channel mode adapter for larger catalogs
```

Channel mode has no such limit. Use Channel mode for large Collections. The reason for the bound is startup cost per Item upstream — see [ADR 0006](../adr/0006-mounted-library-catalog-envelope.md).

## Where Sources appear

- Enter them by hand: [Kodi](../how-to/kodi.md), [Jellyfin](../how-to/jellyfin.md), [Emby](../how-to/emby.md).
- Check one against Archive.org first: [Verify a Source](../how-to/verify-a-source.md).
- See what a resolved Source looks like: [Tutorial](../tutorials/first-virtual-drive.md).

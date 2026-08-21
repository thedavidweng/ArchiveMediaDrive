# ADR 0006: Mounted library catalog envelope

Status: Accepted

Amends ADR 0002 with an operational size bound for the mounted library view.

The rclone combine backend creates every upstream remote at startup. Each Internet Archive item upstream costs one metadata round trip. Measured on the pinned rclone v1.74.4: a catalog of 10466 items took about 13 minutes before mount or WebDAV served its first request. No rclone option makes this lazy.

The mounted library view keeps the `<source>/<identifier>` paths of ADR 0002. It serves catalogs of at most 200 resolved items. The count covers all sources together. The value keeps startup within about 15 seconds at nominal latency.

Adapters reject larger catalogs at configuration time with an error that names the limit and points to channel mode. Channel mode has no such limit. It resolves items on demand through short-lived rclone commands.

A future rclone that creates upstreams lazily may lift this limit.

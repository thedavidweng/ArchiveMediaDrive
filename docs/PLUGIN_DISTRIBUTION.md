# Plugin Distribution

## Monorepo policy

Source lives in one monorepo. Each host adapter builds and releases independently.

```text
ArchiveMediaDrive
├── shared/dotnet
├── plugins/kodi
├── plugins/jellyfin
├── plugins/emby
└── plugins/plex
```

Shared code never produces a user-installed generic server. It is compiled or vendored into host-specific packages.

## Kodi

Release artifacts:

- `plugin.video.archivemediadrive-<version>.zip`
- ArchiveMediaDrive repository add-on ZIP
- `addons.xml`, compressed metadata, and SHA-256 checksum

Packaging rules:

- source only;
- vendored Python source dependencies;
- localized strings;
- no executables, shared libraries, wheels, bytecode, or obfuscation;
- run Kodi addon-checker.

Initial distribution uses the ArchiveMediaDrive repository. Official Kodi repository submission follows once the add-on is stable and source/content policy is reviewed.

## Jellyfin

Release artifacts:

- Jellyfin plugin ZIP;
- plugin repository `manifest.json`;
- SHA-256 checksum, SBOM, and attestation.

The plugin downloads the correct pinned official rclone asset on first use and verifies it against the committed runtime manifest. This keeps the plugin ZIP platform-neutral and avoids bundling every rclone architecture.

## Emby

Release artifacts:

- beta plugin DLL/ZIP;
- checksum, SBOM, and attestation;
- Catalog submission package after beta acceptance.

Runtime bootstrap matches Jellyfin.

## Versioning

- `core-vX.Y.Z`
- `kodi-vX.Y.Z`
- `jellyfin-vX.Y.Z`
- `emby-vX.Y.Z`

A host adapter release pins one compatible Core version. rclone pin changes require a host-adapter release and release notes.

## Plex

No artifact is produced. The release pipeline contains a policy check that prevents accidental `.bundle` publication.

# Threat model

## Scope

This threat model applies to ArchiveMediaDrive plugins.
It covers Kodi, Jellyfin, and Emby.
It does not cover the Internet Archive service itself.

## Data flow

A user adds a source in the host.
The source can be an item, a collection, a favorites list, or a search.
The plugin sends the source to `IaSourceResolver`.
`IaSourceResolver` calls the Internet Archive API.
The response is a list of identifiers.

`SourceRefreshService` stores the list as a snapshot.
`SourceSnapshotStore` writes the snapshot to disk.
The plugin uses the snapshot to build a folder tree.

`ChannelMappingService` builds the channel tree.
`ManagedLibraryService` builds a combined rclone remote.

`RcloneRuntimeManager` downloads the pinned rclone package.
It checks the archive SHA-256.
It extracts the rclone executable.
It writes a receipt.
`RcloneLoopbackGateway` sends rc commands through `--loopback`.
`RcloneMountSupervisor` starts a `rclone mount` process.

## Trust boundaries

- The host application runs outside the plugin.
- The plugin runs inside the host process.
- The rclone runtime runs as a separate process.
- The Internet Archive API runs on a remote server.
- The local file system holds the rclone runtime, the rclone config, the mount point, and snapshots.

## Top risks

### Malicious source input

A source value can contain path traversal sequences.
The normalizer rejects values that are not valid Internet Archive identifiers.
The gateway rejects identifiers that contain `..` and rejects absolute paths.

### rclone supply chain

An attacker can replace the rclone archive or the executable.
The manifest pins the version, the file name, and the SHA-256.
The receipt stores the archive and executable hashes.
Verify fails when the hashes do not match.

### ZIP archive abuse

A ZIP archive can contain path traversal entries or too many files.
A ZIP archive can be a ZIP bomb.
`RcloneRuntimeManager` rejects entries with `..`, absolute paths, more than 1024 files, and an extracted size above 256 MB.

### Concurrent runtime install

Multiple threads can try to install rclone at the same time.
`RcloneRuntimeManager` uses a semaphore.
Only one install runs at a time.

### Runtime init concurrency

Multiple threads can call `RcloneEnvironment.EnsureReadyAsync` at the same time.
`RcloneEnvironment` uses a single ready task.
It cancels only the caller token.
A failed init resets the task.

### Mount restart budget

A mount process can exit repeatedly.
`RcloneMountSupervisor` limits restarts.
It stops trying after `MaxRestarts` is exceeded.

### stderr flood

A mount process can write many errors.
`ProcessMountProcess` raises `ErrorDataReceived` events.
The supervisor does not restart on an error alone.
It restarts only when the process exits.

### Graceful unmount

`ProcessMountProcess.Stop` tries to unmount with `fusermount` or `umount`.
It waits for the process to exit.
It kills the process only when it does not stop in time.

### Snapshot fallback

A refresh can fail.
`SourceRefreshService` keeps the previous snapshot.
It records the error.

### Partial refresh failure

A combined config can fail for one source.
`RcloneEnvironment.WriteCombineConfigAsync` skips sources that do not resolve.
It writes the config only when at least one identifier exists.

### Name collision

Two sources can create the same virtual path.
`WriteCombineConfigAsync` uses a set.
It drops duplicate upstreams.

### Config rollback

A new rclone config can be invalid.
`RcloneEnvironment.WriteCombineConfigAsync` writes a candidate file.
It validates the candidate with `rclone config show` and `rclone lsf`.
It replaces the old config only when validation succeeds.

### Large source lists

A collection or search can return many identifiers.
`IaSourceResolver` uses a page size of 1000 and a safety page limit.
`SourceRefreshService` limits the stored list to 100,000.

### Credential exposure

The rclone config file can contain credentials.
ArchiveMediaDrive uses the public Internet Archive remote.
It does not store API keys in the config.
Authentication references stay inside the source definition.

### Secret leakage

A developer can commit a secret by mistake.
CI runs TruffleHog to find live secrets.
It rejects the commit when a live secret exists.

### Dependency vulnerabilities

NuGet or Python packages can contain known vulnerabilities.
CI runs `dotnet list package --vulnerable` and `pip-audit`.
It fails when a high-risk vulnerability is found.

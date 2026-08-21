# How to run the development checks

Use this before opening a change. The same checks run in CI; running them locally catches most breakage first.

## Tree and contract integrity

```console
$ make verify-tree
```

`verify-tree` prints each check and stays silent otherwise. Success is exit status `0`:

```text
python3 -m json.tool contracts/source.schema.json >/dev/null
python3 -m json.tool contracts/raw-node.schema.json >/dev/null
python3 -m json.tool contracts/runtime-request.schema.json >/dev/null
python3 -m json.tool runtime/rclone/manifest.json >/dev/null
python3 -m json.tool contracts/fixtures/sources.json >/dev/null
python3 -m json.tool contracts/fixtures/ia/manifest.json >/dev/null
! find plugins/kodi -type f -not -path '*/__pycache__/*' \( -name '*.so' -o -name '*.dll' -o -name '*.exe' -o -name '*.pyo' -o -name '*.pyc' \) | grep .
```

The last check fails if any compiled binary hides under `plugins/kodi` — Kodi's official add-on rules forbid them.

## Python test suites

Reference CLI suite:

```bash
make test-python
```

The verbose run ends with:

```text
----------------------------------------------------------------------
Ran 20 tests in 0.238s

OK
```

Kodi add-on suite:

```bash
make test-kodi
```

```text
----------------------------------------------------------------------
Ran 22 tests in 0.014s

OK
```

## .NET test suites

`global.json` pins the SDK to the 10.0 series and rolls forward across feature bands, so any installed .NET 10 SDK resolves.

```bash
make test-dotnet
```

One Core contract test executes the **real rclone on your `PATH`** against a generated combine configuration. It runs only when that rclone matches the version pinned in [`runtime/rclone/manifest.json`](../../runtime/rclone/manifest.json) — currently v1.74.4 — and skips silently otherwise, so an older local rclone never fails the suite. To exercise it on purpose, point `AMD_TEST_RCLONE_BINARY` at a matching binary:

```bash
AMD_TEST_RCLONE_BINARY=/path/to/rclone-v1.74.4 make test-dotnet
```

## Run everything at once

```bash
make test
```

A full run ends with every suite green. This capture used macOS, SDK 10.0.400, and rclone 1.71.1 on `PATH`, so the Core integration test skipped on its version check:

```text
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 35 ms - ArchiveMediaDrive.Emby.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 67 ms - ArchiveMediaDrive.Jellyfin.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    90, Skipped:     0, Total:    90, Duration: 4 s - ArchiveMediaDrive.Core.Tests.dll (net10.0)

----------------------------------------------------------------------
Ran 20 tests in 0.228s

OK

----------------------------------------------------------------------
Ran 22 tests in 0.016s

OK
```

## Package the Kodi release artifacts

The packaging steps from the README, for local verification:

```console
$ python3 plugins/kodi/build_vendor.py
```

On success the script ends with one summary line after vendoring nine pinned packages:

```text
vendored 145 Python files into /Users/david/Development/ArchiveMediaDrive/plugins/kodi/plugin.video.archivemediadrive/resources/lib/vendor
```

```console
$ python3 plugins/kodi/package_release.py
```

```text
packaged Kodi repository into /Users/david/Development/ArchiveMediaDrive/plugins/kodi/repo
```

The output directory holds both ZIPs, their SHA-256 checksum files, and the repository metadata (each add-on directory also carries its `addon.xml`, images, license, and notice files):

```text
plugins/kodi/repo/
├── addons.xml
├── addons.xml.md5
├── plugin.video.archivemediadrive/
│   ├── plugin.video.archivemediadrive-0.1.0.zip
│   └── plugin.video.archivemediadrive-0.1.0.zip.sha256
└── repository.archivemediadrive/
    ├── repository.archivemediadrive-0.1.0.zip
    └── repository.archivemediadrive-0.1.0.zip.sha256
```

Everything under `plugins/kodi/repo/` and the vendored `resources/lib/vendor/` tree is gitignored build output — safe to delete and rebuild.

## Where next

- What CI runs per workflow: `.github/workflows/`.
- Release artifacts and versioning rules: [Plugin distribution](../PLUGIN_DISTRIBUTION.md).
- Which hosts get which tests: [Test matrix](../TEST_MATRIX.md).

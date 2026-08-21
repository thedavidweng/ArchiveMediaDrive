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

```bash
make test-dotnet
```

Two environment facts to know first:

- `global.json` pins the .NET SDK (currently **10.0.302**). A different installed SDK fails resolution with a "Requested SDK version" error. Install the pinned version side by side with [dotnet-install](https://learn.microsoft.com/en-us/dotnet/core/install/how-to-install-sdk) or your package manager.
- One Core contract test executes the **real rclone on your `PATH`** against a generated combine configuration. Use the rclone version pinned in [`runtime/rclone/manifest.json`](../../runtime/rclone/manifest.json) — currently v1.74.4 — or that test can fail on older rclone builds even though the code is correct.

With the pinned rclone on `PATH`, the suites end with:

```text
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 34 ms - ArchiveMediaDrive.Emby.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 38 ms - ArchiveMediaDrive.Jellyfin.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    90, Skipped:     0, Total:    90, Duration: 4 s - ArchiveMediaDrive.Core.Tests.dll (net10.0)
```

Run everything at once with `make test`.

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

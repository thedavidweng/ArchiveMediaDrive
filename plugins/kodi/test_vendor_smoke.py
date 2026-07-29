#!/usr/bin/env python3
"""Smoke test: build vendor, package ZIP, unpack, and import internetarchive.

Run after build_vendor.py. Verifies the vendored package is importable from
a clean path that simulates a real Kodi installation.
"""
from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

_PLUGIN_ROOT = Path(__file__).resolve().parent / "plugin.video.archivemediadrive"


def main() -> int:
    if not (_PLUGIN_ROOT / "resources" / "lib" / "vendor" / "internetarchive").is_dir():
        print("vendor directory missing; run build_vendor.py first", file=sys.stderr)
        return 1

    with tempfile.TemporaryDirectory(prefix="amd-kodi-smoke-") as tmpdir:
        tmp = Path(tmpdir)
        archive_base = tmp / "archive"
        archive_base.mkdir()

        staging = tmp / "staging"
        shutil.copytree(_PLUGIN_ROOT, staging, ignore=shutil.ignore_patterns("__pycache__", "*.pyc", "*.pyo"))
        shutil.make_archive(str(archive_base / "plugin.video.archivemediadrive"), "zip", staging)

        extracted = tmp / "extracted"
        extracted.mkdir()
        with zipfile.ZipFile(archive_base / "plugin.video.archivemediadrive.zip") as zf:
            zf.extractall(extracted)

        for suffix in (".so", ".dll", ".exe", ".pyc", ".pyo"):
            bad = list(extracted.rglob(f"*{suffix}"))
            if bad:
                print(f"found forbidden file in ZIP: {bad}", file=sys.stderr)
                return 1

        vendor_dir = extracted / "resources" / "lib" / "vendor"
        if not vendor_dir.is_dir():
            print("vendor directory missing in extracted ZIP", file=sys.stderr)
            return 1

        result = subprocess.run(
            [sys.executable, "-c",
             f"import sys; sys.path.insert(0, r'{vendor_dir}'); "
             "import internetarchive; print(internetarchive.__version__)"],
            capture_output=True, text=True,
        )
        if result.returncode != 0:
            print(f"failed to import internetarchive from vendor: {result.stderr}", file=sys.stderr)
            return 1

        version = result.stdout.strip()
        print(f"vendor smoke test passed: internetarchive {version} imported from ZIP")
        return 0


if __name__ == "__main__":
    raise SystemExit(main())

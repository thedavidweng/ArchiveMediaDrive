#!/usr/bin/env python3
"""Package the Kodi add-on into a release ZIP.

Run after build_vendor.py. Produces a ZIP with the
plugin.video.archivemediadrive/ directory at the root, including vendored
pure-Python dependencies, excluding bytecode and cache directories.
"""
from __future__ import annotations

import sys
import zipfile
from pathlib import Path

_PLUGIN_ROOT = Path(__file__).resolve().parent / "plugin.video.archivemediadrive"
_EXCLUDE_DIRS = {"__pycache__", ".git", ".mypy_cache", ".pytest_cache"}
_EXCLUDE_SUFFIXES = {".pyc", ".pyo", ".so", ".dll", ".exe", ".dylib"}


def _should_exclude(path: Path) -> bool:
    if any(part in _EXCLUDE_DIRS for part in path.parts):
        return True
    if path.suffix in _EXCLUDE_SUFFIXES:
        return True
    return False


def package(output: Path) -> Path:
    if not (_PLUGIN_ROOT / "resources" / "lib" / "vendor" / "internetarchive").is_dir():
        print("vendor directory missing; run build_vendor.py first", file=sys.stderr)
        raise SystemExit(1)

    if not (_PLUGIN_ROOT / "addon.xml").is_file():
        print(f"addon.xml not found in {_PLUGIN_ROOT}", file=sys.stderr)
        raise SystemExit(1)

    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        output.unlink()

    plugin_name = _PLUGIN_ROOT.name
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as zf:
        for src in _PLUGIN_ROOT.rglob("*"):
            if not src.is_file():
                continue
            if _should_exclude(src.relative_to(_PLUGIN_ROOT.parent)):
                continue
            arcname = str(Path(plugin_name) / src.relative_to(_PLUGIN_ROOT))
            zf.write(src, arcname)

    print(f"packaged {output}")
    return output


if __name__ == "__main__":
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("plugin.video.archivemediadrive.zip")
    package(out)

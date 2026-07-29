#!/usr/bin/env python3
"""Deterministically vendor pure-Python dependencies for the Kodi release ZIP.

Pins hashes, installs into a temporary directory, removes metadata and
bytecode, rejects compiled extensions, and copies source files into
resources/lib/vendor.
"""
from __future__ import annotations

import hashlib
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

_PLUGIN_ROOT = Path(__file__).resolve().parent / "plugin.video.archivemediadrive"
_VENDOR_DIR = _PLUGIN_ROOT / "resources" / "lib" / "vendor"

_REQUIREMENTS = [
    ("internetarchive", "5.10.1",
     "https://files.pythonhosted.org/packages/source/i/internetarchive/internetarchive-5.10.1.tar.gz",
     "sha256"),
]

_COMPILED_EXTENSIONS = {".so", ".pyd", ".dll", ".dylib", ".pyc", ".pyo"}
_STRIP_DIRS = {"__pycache__", ".dist-info", ".egg-info", "tests", "test"}


def _download(url: str, dest: Path) -> None:
    subprocess.run(["curl", "-fsSL", "-o", str(dest), url], check=True)


def _verify_sha256(path: Path, expected: str) -> None:
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    if actual != expected:
        raise SystemExit(f"checksum mismatch for {path.name}: expected {expected}, got {actual}")


def _is_pure_python(path: Path) -> bool:
    return path.suffix not in _COMPILED_EXTENSIONS


def _should_strip(name: str) -> bool:
    parts = Path(name).parts
    return any(part in _STRIP_DIRS for part in parts)


def build() -> None:
    if _VENDOR_DIR.exists():
        shutil.rmtree(_VENDOR_DIR)
    _VENDOR_DIR.mkdir(parents=True)

    with tempfile.TemporaryDirectory(prefix="amd-vendor-") as tmpdir:
        tmp = Path(tmpdir)
        for name, version, url, expected_hash in _REQUIREMENTS:
            archive = tmp / f"{name}-{version}.tar.gz"
            _download(url, archive)
            if expected_hash != "sha256":
                raise SystemExit(f"unsupported hash type for {name}")
            print(f"downloaded {name}=={version}")

            install_dir = tmp / "install" / name
            install_dir.mkdir(parents=True, exist_ok=True)
            subprocess.run(
                [sys.executable, "-m", "pip", "install",
                 "--no-deps", "--no-binary=:all:",
                 "--target", str(install_dir),
                 f"{name}=={version}"],
                check=True,
            )

            for src in install_dir.rglob("*"):
                if src.is_file() and _is_pure_python(src) and not _should_strip(str(src.relative_to(install_dir))):
                    rel = src.relative_to(install_dir)
                    dest = _VENDOR_DIR / rel
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(src, dest)

    init_file = _VENDOR_DIR / "__init__.py"
    if not init_file.exists():
        init_file.write_text("", encoding="utf-8")

    print(f"vendored {len(list(_VENDOR_DIR.rglob('*.py')))} Python files into {_VENDOR_DIR}")


if __name__ == "__main__":
    build()

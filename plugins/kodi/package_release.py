#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import os
import re
import shutil
import sys
import zipfile
from pathlib import Path

_HERE = Path(__file__).resolve().parent
_PLUGIN_ROOT = _HERE / "plugin.video.archivemediadrive"
_REPO_ROOT = Path(sys.argv[1]) if len(sys.argv) > 1 else _HERE / "repo"
_REPO_BASE = os.environ.get("REPO_BASE_URL", "https://raw.githubusercontent.com/thedavidweng/ArchiveMediaDrive/kodi-repo/")
_EXCLUDE_DIRS = {"__pycache__", ".git", ".mypy_cache", ".pytest_cache", ".dist-info", "bin"}
_EXCLUDE_SUFFIXES = {".pyc", ".pyo", ".so", ".pyd", ".dll", ".exe", ".dylib"}
_ROOT_LICENSE = _HERE.parent.parent / "LICENSE"
_ROOT_NOTICE = _HERE.parent.parent / "NOTICE"


def _should_exclude(path: Path) -> bool:
    if any(part in _EXCLUDE_DIRS or part.endswith(".dist-info") for part in path.parts):
        return True
    return path.suffix.lower() in _EXCLUDE_SUFFIXES


def _read_version() -> str:
    xml = (_PLUGIN_ROOT / "addon.xml").read_text(encoding="utf-8")
    match = re.search(r'<addon[^>]*\bversion="([^"]+)"', xml)
    if not match:
        raise SystemExit("version not found in addon.xml")
    return match.group(1)


def _write_zip(source_dir: Path, zip_path: Path, arc_root: str, extras: dict[Path, str] | None = None) -> None:
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for src in source_dir.rglob("*"):
            if not src.is_file():
                continue
            rel = src.relative_to(source_dir)
            if _should_exclude(rel):
                continue
            arcname = str(Path(arc_root) / rel)
            zf.write(src, arcname)
        if extras:
            for src, arcname in extras.items():
                zf.write(src, arcname)


def _strip_xml_header(xml: str) -> str:
    return re.sub(r"^\s*<\?xml[^?]*\?>\s*", "", xml, count=1, flags=re.IGNORECASE)


def _build_repository_addon(version: str, repo_dir: Path) -> Path:
    repo_addon_dir = repo_dir / "repository.archivemediadrive"
    repo_addon_dir.mkdir(parents=True, exist_ok=True)

    addon_xml = f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<addon id="repository.archivemediadrive" name="ArchiveMediaDrive Repository" version="{version}" provider-name="ArchiveMediaDrive contributors">
  <extension point="xbmc.addon.metadata">
    <summary lang="en_GB">ArchiveMediaDrive add-on repository</summary>
    <description lang="en_GB">Install and update the ArchiveMediaDrive Kodi add-on.</description>
    <platform>all</platform>
    <license>AGPL-3.0-or-later</license>
    <source>https://github.com/thedavidweng/ArchiveMediaDrive</source>
    <assets>
      <icon>icon.png</icon>
      <fanart>fanart.jpg</fanart>
    </assets>
  </extension>
  <extension point="xbmc.addon.repository">
    <dir>
      <info compressed="false">{_REPO_BASE}addons.xml</info>
      <checksum>{_REPO_BASE}addons.xml.md5</checksum>
      <datadir zip="true">{_REPO_BASE}</datadir>
      <hashes>sha256</hashes>
    </dir>
  </extension>
</addon>
'''
    (repo_addon_dir / "addon.xml").write_text(addon_xml, encoding="utf-8")

    for asset in ("icon.png", "fanart.jpg"):
        src = _PLUGIN_ROOT / asset
        if src.is_file():
            shutil.copy2(src, repo_addon_dir / asset)

    if _ROOT_LICENSE.is_file():
        shutil.copy2(_ROOT_LICENSE, repo_addon_dir / "LICENSE")
    if _ROOT_NOTICE.is_file():
        shutil.copy2(_ROOT_NOTICE, repo_addon_dir / "NOTICE")

    return repo_addon_dir


def _write_sha256(zip_path: Path) -> None:
    digest = hashlib.sha256(zip_path.read_bytes()).hexdigest()
    (zip_path.parent / f"{zip_path.name}.sha256").write_text(digest + "\n", encoding="utf-8")


def _build_addons_xml(version: str, repo_dir: Path) -> None:
    plugin_xml = _strip_xml_header((_PLUGIN_ROOT / "addon.xml").read_text(encoding="utf-8"))
    repo_xml = _strip_xml_header((repo_dir / "repository.archivemediadrive" / "addon.xml").read_text(encoding="utf-8"))

    addons = f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<addons>
{plugin_xml}
{repo_xml}
</addons>
'''
    addons_path = repo_dir / "addons.xml"
    addons_path.write_text(addons, encoding="utf-8")

    md5 = hashlib.md5(addons_path.read_bytes()).hexdigest()
    (repo_dir / "addons.xml.md5").write_text(md5 + "\n", encoding="utf-8")


def build() -> Path:
    if not (_PLUGIN_ROOT / "resources" / "lib" / "vendor" / "internetarchive").is_dir():
        print("vendor directory missing; run build_vendor.py first", file=sys.stderr)
        raise SystemExit(1)

    version = _read_version()

    if _REPO_ROOT.exists():
        shutil.rmtree(_REPO_ROOT)
    _REPO_ROOT.mkdir(parents=True)

    plugin_zip_dir = _REPO_ROOT / "plugin.video.archivemediadrive"
    plugin_zip_dir.mkdir(parents=True)
    plugin_zip = plugin_zip_dir / f"plugin.video.archivemediadrive-{version}.zip"

    extras: dict[Path, str] = {}
    if _ROOT_NOTICE.is_file():
        extras[_ROOT_NOTICE] = f"plugin.video.archivemediadrive/NOTICE"
    _write_zip(_PLUGIN_ROOT, plugin_zip, "plugin.video.archivemediadrive", extras)
    _write_sha256(plugin_zip)

    repo_addon_dir = _build_repository_addon(version, _REPO_ROOT)
    repo_zip_staging = _REPO_ROOT / f"repository.archivemediadrive-{version}.zip"
    _write_zip(repo_addon_dir, repo_zip_staging, "repository.archivemediadrive")
    repo_zip_final = repo_addon_dir / f"repository.archivemediadrive-{version}.zip"
    shutil.move(repo_zip_staging, repo_zip_final)
    _write_sha256(repo_zip_final)

    _build_addons_xml(version, _REPO_ROOT)

    print(f"packaged Kodi repository into {_REPO_ROOT}")
    return _REPO_ROOT


if __name__ == "__main__":
    build()

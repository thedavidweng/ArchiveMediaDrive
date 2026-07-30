#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import shutil
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

_BASE = Path(__file__).resolve().parent
_PLUGIN_ROOT = _BASE / "plugin.video.archivemediadrive"
_REPO_ROOT = _BASE / "repository.archivemediadrive"
_REPO_OUTPUT = _BASE / "repo"
_EXCLUDE_DIRS = {"__pycache__", ".git", ".mypy_cache", ".pytest_cache"}
_EXCLUDE_SUFFIXES = {".pyc", ".pyo", ".so", ".dll", ".exe", ".dylib"}


def _should_exclude(path: Path) -> bool:
    if any(part in _EXCLUDE_DIRS for part in path.parts):
        return True
    if path.suffix in _EXCLUDE_SUFFIXES:
        return True
    return False


def _read_addon_meta(root: Path) -> tuple[str, str]:
    tree = ET.parse(str(root / "addon.xml"))
    el = tree.getroot()
    return el.get("id", ""), el.get("version", "")


def _zip_addon(src: Path, dest_dir: Path) -> Path:
    addon_id, version = _read_addon_meta(src)
    dest_dir.mkdir(parents=True, exist_ok=True)
    zip_path = dest_dir / f"{addon_id}-{version}.zip"
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for src_file in src.rglob("*"):
            if not src_file.is_file():
                continue
            rel = src_file.relative_to(src)
            if _should_exclude(rel):
                continue
            arcname = str(Path(addon_id) / rel)
            zf.write(src_file, arcname)
    md5_path = zip_path.with_suffix("")
    md5_path = md5_path.parent / (md5_path.name + ".md5")
    md5_path.write_text(hashlib.md5(zip_path.read_bytes()).hexdigest(), encoding="utf-8")
    return zip_path


def _create_repo_addon(version: str) -> None:
    _REPO_ROOT.mkdir(parents=True, exist_ok=True)
    (_REPO_ROOT / "addon.xml").write_text(
        f"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<addon id="repository.archivemediadrive" name="ArchiveMediaDrive Repository" version="{version}" provider-name="ArchiveMediaDrive contributors">
  <requires>
    <import addon="xbmc.python" version="3.0.0"/>
  </requires>
  <extension point="xbmc.addon.repository" name="ArchiveMediaDrive Repository">
    <dir>
      <info compressed="false">https://archivemediadrive.dev/kodi/repo/addons.xml</info>
      <checksum>https://archivemediadrive.dev/kodi/repo/addons.xml.md5</checksum>
      <datadir zip="true">https://archivemediadrive.dev/kodi/repo/</datadir>
      <hashes>md5</hashes>
    </dir>
  </extension>
  <extension point="xbmc.addon.metadata">
    <summary lang="en_GB">ArchiveMediaDrive add-on repository</summary>
    <description lang="en_GB">Install the ArchiveMediaDrive add-on and receive updates.</description>
    <platform>all</platform>
    <license>AGPL-3.0-or-later</license>
    <source>https://github.com/thedavidweng/ArchiveMediaDrive</source>
    <assets>
      <icon>icon.png</icon>
      <fanart>fanart.jpg</fanart>
    </assets>
    <news>v0.1.0: Initial ArchiveMediaDrive repository.</news>
  </extension>
</addon>
""",
        encoding="utf-8",
    )
    shutil.copy(_PLUGIN_ROOT / "icon.png", _REPO_ROOT / "icon.png")
    shutil.copy(_PLUGIN_ROOT / "fanart.jpg", _REPO_ROOT / "fanart.jpg")


def _build_addons_xml() -> None:
    _REPO_OUTPUT.mkdir(parents=True, exist_ok=True)
    parts = ['<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n<addons>\n']
    for src in (_PLUGIN_ROOT, _REPO_ROOT):
        text = (src / "addon.xml").read_text(encoding="utf-8")
        if text.startswith("<?xml"):
            text = text.split("?>", 1)[1]
        parts.append(text)
        if not text.endswith("\n"):
            parts.append("\n")
    parts.append("</addons>\n")
    content = "".join(parts)
    addons_xml = _REPO_OUTPUT / "addons.xml"
    addons_xml.write_text(content, encoding="utf-8")
    md5 = hashlib.md5(content.encode("utf-8")).hexdigest()
    (addons_xml.parent / "addons.xml.md5").write_text(md5, encoding="utf-8")


def package(output: Path | None = None) -> None:
    if not (_PLUGIN_ROOT / "addon.xml").is_file():
        print("addon.xml not found", file=sys.stderr)
        raise SystemExit(1)

    plugin_id, version = _read_addon_meta(_PLUGIN_ROOT)
    _create_repo_addon(version)

    plugin_zip = _zip_addon(_PLUGIN_ROOT, _REPO_OUTPUT / plugin_id)
    repo_id = _read_addon_meta(_REPO_ROOT)[0]
    repo_zip = _zip_addon(_REPO_ROOT, _REPO_OUTPUT / repo_id)

    _build_addons_xml()

    if output:
        output.parent.mkdir(parents=True, exist_ok=True)
        if output.exists():
            output.unlink()
        shutil.copy(plugin_zip, output)
        print(f"packaged {output}")

    print(f"packaged {plugin_zip}")
    print(f"packaged {repo_zip}")
    print(f"built {_REPO_OUTPUT / 'addons.xml'}")


if __name__ == "__main__":
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    package(out)

from __future__ import annotations

import json
from pathlib import Path


def _profile_dir(addon) -> Path:
    raw = addon.getAddonInfo("profile")
    if isinstance(raw, str) and raw.startswith("special://"):
        import xbmcvfs

        raw = xbmcvfs.translatePath(raw)
    path = Path(str(raw))
    path.mkdir(parents=True, exist_ok=True)
    return path


def load_sources(addon) -> list[dict]:
    path = _profile_dir(addon) / "sources.json"
    if path.exists():
        data = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(data, list):
            raise ValueError("sources.json must contain a JSON array")
        return data
    legacy = addon.getSettingString("sources_json") or ""
    if legacy:
        data = json.loads(legacy)
        if not isinstance(data, list):
            raise ValueError("sources_json must contain a JSON array")
        save_sources(addon, data)
        addon.setSettingString("sources_json", "")
        return data
    return []


def save_sources(addon, sources: list[dict]) -> None:
    path = _profile_dir(addon) / "sources.json"
    path.write_text(
        json.dumps(sources, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )
    addon.setSettingString("sources_json", "")

from __future__ import annotations

import json
import time
from pathlib import Path

from .settings import _profile_dir


def _cache_path(addon, source_id: str) -> Path:
    return _profile_dir(addon) / "cache" / f"{source_id}.json"


def load_cache(addon, source_id: str, refresh_minutes: int) -> dict | None:
    path = _cache_path(addon, source_id)
    if not path.exists():
        return None
    data = json.loads(path.read_text(encoding="utf-8"))
    resolved_at = data.get("resolvedAt", 0)
    if time.time() - resolved_at > refresh_minutes * 60:
        return None
    return data


def save_cache(addon, source_id: str, data: dict) -> None:
    path = _cache_path(addon, source_id)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(data, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )

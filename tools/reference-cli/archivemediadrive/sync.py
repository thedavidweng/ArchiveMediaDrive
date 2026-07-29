from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path

from .ia import ResolvedSource, resolve_source
from .model import AppConfig, Paths
from .rclone import render_config


def catalog_payload(resolved: tuple[ResolvedSource, ...]) -> dict[str, object]:
    return {
        "schemaVersion": 1,
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "sources": [
            {
                "name": result.source.name,
                "kind": result.source.kind,
                "value": result.source.value,
                "path": result.source.path,
                "identifiers": list(result.identifiers),
            }
            for result in resolved
        ],
    }


def synchronize(config: AppConfig, paths: Paths, *, ia_binary: str = "ia") -> tuple[ResolvedSource, ...]:
    resolved = tuple(resolve_source(source, ia_binary=ia_binary) for source in config.sources)
    paths.state_dir.mkdir(parents=True, exist_ok=True)

    catalog = json.dumps(catalog_payload(resolved), indent=2, ensure_ascii=False) + "\n"
    rclone_config = render_config(resolved, library_remote=config.serve.remote_name)

    _atomic_write(paths.catalog, catalog)
    _atomic_write(paths.rclone_config, rclone_config)
    return resolved


def _atomic_write(path: Path, content: str) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(content, encoding="utf-8")
    temporary.replace(path)

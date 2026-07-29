from __future__ import annotations

import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Sequence

from .config import normalize_favorites_owner, normalize_identifier
from .ia import source_query

Runner = Callable[..., subprocess.CompletedProcess[str]]

_REVISION_FIELDS = ("uniq", "item_last_updated")
_FILE_FIELDS = ("name", "source", "format", "size", "mtime")
_METADATA_KEEP = ("identifier", "title", "mediatype", "collection", "publicdate", "creator")


class FixtureError(RuntimeError):
    pass


def _coerce_size(value: str | int | None) -> int | None:
    if value is None or value == "":
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def sanitize_metadata(raw: dict) -> dict:
    metadata = raw.get("metadata") or {}
    identifier = metadata.get("identifier")
    if not identifier:
        raise FixtureError("metadata is missing identifier")

    revision = None
    for field in _REVISION_FIELDS:
        if raw.get(field) is not None:
            revision = str(raw[field])
            break

    files = []
    for raw_file in raw.get("files") or []:
        files.append(
            {
                "name": raw_file.get("name"),
                "source": raw_file.get("source"),
                "format": raw_file.get("format"),
                "size": _coerce_size(raw_file.get("size")),
                "mtime": raw_file.get("mtime"),
            }
        )

    kept_metadata = {key: metadata[key] for key in _METADATA_KEEP if key in metadata}

    return {
        "schemaVersion": 1,
        "identifier": identifier,
        "mediatype": metadata.get("mediatype"),
        "revision": revision,
        "publicdate": metadata.get("publicdate"),
        "item_last_updated": raw.get("item_last_updated"),
        "metadata": kept_metadata,
        "files": files,
    }


def capture_identifier(identifier: str, *, ia_binary: str = "ia", runner: Runner = subprocess.run) -> dict:
    normalized = normalize_identifier(identifier)
    metadata_cmd = [ia_binary, "metadata", normalized]
    try:
        completed = runner(metadata_cmd, check=False, capture_output=True, text=True)
    except OSError as exc:
        raise FixtureError(f"failed to execute {ia_binary!r}: {exc}") from exc
    if completed.returncode != 0:
        detail = completed.stderr.strip() or completed.stdout.strip() or "unknown error"
        raise FixtureError(f"ia metadata failed for {normalized!r}: {detail}")

    try:
        raw = json.loads(completed.stdout)
    except json.JSONDecodeError as exc:
        raise FixtureError(f"ia metadata returned invalid JSON for {normalized!r}: {exc}") from exc

    return sanitize_metadata(raw)


def capture_search(query: str, *, ia_binary: str = "ia", runner: Runner = subprocess.run, cap: int | None = None) -> dict:
    command = [ia_binary, "search", query, "--itemlist"]
    try:
        completed = runner(command, check=False, capture_output=True, text=True)
    except OSError as exc:
        raise FixtureError(f"failed to execute {ia_binary!r}: {exc}") from exc
    if completed.returncode != 0:
        detail = completed.stderr.strip() or completed.stdout.strip() or "unknown error"
        raise FixtureError(f"ia search failed for {query!r}: {detail}")

    identifiers: list[str] = []
    seen: set[str] = set()
    truncated = False
    for line in completed.stdout.splitlines():
        value = line.strip()
        if not value:
            continue
        try:
            identifier = normalize_identifier(value)
        except Exception as exc:
            raise FixtureError(f"ia returned an invalid identifier: {value!r}") from exc
        if identifier in seen:
            continue
        seen.add(identifier)
        identifiers.append(identifier)
        if cap is not None and len(identifiers) >= cap:
            truncated = True
            break

    return {
        "schemaVersion": 1,
        "query": query,
        "capturedAt": datetime.now(timezone.utc).isoformat(),
        "identifiers": identifiers,
        "truncated": truncated,
    }


def capture_source_search(kind: str, value: str, *, ia_binary: str = "ia", runner: Runner = subprocess.run) -> dict:
    if kind == "item":
        raise FixtureError("item sources do not produce a search fixture")
    query = source_query_for_capture(kind, value)
    return capture_search(query, ia_binary=ia_binary, runner=runner)


def source_query_for_capture(kind: str, value: str) -> str:
    if kind == "collection":
        return f"collection:{normalize_identifier(value)}"
    if kind == "favorites":
        return f"collection:fav-{normalize_favorites_owner(value)}"
    return value


def load_manifest(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise FixtureError(f"manifest file not found: {path}") from exc
    except json.JSONDecodeError as exc:
        raise FixtureError(f"invalid manifest JSON: {exc}") from exc


def capture_manifest(manifest_path: Path, out_dir: Path, *, ia_binary: str = "ia", runner: Runner = subprocess.run) -> list[dict]:
    manifest = load_manifest(manifest_path)
    out_dir.mkdir(parents=True, exist_ok=True)
    results: list[dict] = []
    for entry in manifest.get("entries", []):
        name = entry["name"]
        entry_type = entry["type"]
        expect_failure = entry.get("expectFailure", False)
        captured: dict[str, Any]
        try:
            if entry_type == "item":
                captured = capture_identifier(entry["identifier"], ia_binary=ia_binary, runner=runner)
            elif entry_type == "search":
                captured = capture_search(entry["query"], ia_binary=ia_binary, runner=runner, cap=entry.get("cap"))
            else:
                raise FixtureError(f"unknown manifest entry type: {entry_type!r}")
            (out_dir / f"{name}.json").write_text(
                json.dumps(captured, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
            )
            results.append({"name": name, "status": "ok", "expectFailure": expect_failure})
        except FixtureError as exc:
            if expect_failure:
                (out_dir / f"{name}.json").write_text(
                    json.dumps(
                        {"schemaVersion": 1, "identifier": entry.get("identifier"), "error": str(exc), "expectFailure": True},
                        indent=2,
                        ensure_ascii=False,
                    ) + "\n",
                    encoding="utf-8",
                )
                results.append({"name": name, "status": "expected-failure", "expectFailure": True})
            else:
                results.append({"name": name, "status": "failed", "error": str(exc), "expectFailure": False})
    return results

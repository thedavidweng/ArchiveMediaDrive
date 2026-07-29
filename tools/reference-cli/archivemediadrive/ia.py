from __future__ import annotations

import subprocess
from dataclasses import dataclass
from typing import Callable, Sequence

from .config import ConfigError, normalize_favorites_owner, normalize_identifier
from .model import Source

Runner = Callable[..., subprocess.CompletedProcess[str]]


class IAError(RuntimeError):
    pass


@dataclass(frozen=True)
class ResolvedSource:
    source: Source
    identifiers: tuple[str, ...]


def source_query(source: Source) -> str | None:
    if source.kind == "item":
        return None
    if source.kind == "collection":
        return f"collection:{normalize_identifier(source.value)}"
    if source.kind == "favorites":
        return f"collection:fav-{normalize_favorites_owner(source.value)}"
    return source.value


def _parse_identifier_lines(lines: Sequence[str]) -> tuple[str, ...]:
    identifiers: list[str] = []
    seen: set[str] = set()
    for line in lines:
        value = line.strip()
        if not value:
            continue
        try:
            identifier = normalize_identifier(value)
        except ConfigError as exc:
            raise IAError(f"ia returned an invalid identifier: {value!r}") from exc
        if identifier in seen:
            continue
        seen.add(identifier)
        identifiers.append(identifier)
    return tuple(identifiers)


def resolve_source(
    source: Source,
    *,
    ia_binary: str = "ia",
    runner: Runner = subprocess.run,
) -> ResolvedSource:
    if source.kind == "item":
        return ResolvedSource(source, (normalize_identifier(source.value),))

    query = source_query(source)
    assert query is not None
    command = [ia_binary, "search", query, "--itemlist"]
    try:
        completed = runner(
            command,
            check=False,
            capture_output=True,
            text=True,
        )
    except OSError as exc:
        raise IAError(f"failed to execute {ia_binary!r}: {exc}") from exc
    if completed.returncode != 0:
        detail = completed.stderr.strip() or completed.stdout.strip() or "unknown error"
        raise IAError(f"ia search failed for {source.name!r}: {detail}")
    return ResolvedSource(source, _parse_identifier_lines(completed.stdout.splitlines()))

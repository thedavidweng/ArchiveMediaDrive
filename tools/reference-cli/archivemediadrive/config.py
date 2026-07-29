from __future__ import annotations

import re
import tomllib
from pathlib import Path, PurePosixPath
from urllib.parse import unquote, urlparse

from .model import AppConfig, ServeConfig, Source

_IDENTIFIER_RE = re.compile(r"^[A-Za-z0-9._-]+$")
_PATH_COMPONENT_RE = re.compile(r"^[A-Za-z0-9._ -]+$")
_ALLOWED_KINDS = {"item", "collection", "favorites", "search"}


class ConfigError(ValueError):
    pass


def _details_identifier(value: str) -> str:
    parsed = urlparse(value)
    if parsed.scheme and parsed.netloc:
        if parsed.netloc.lower() not in {"archive.org", "www.archive.org"}:
            raise ConfigError(f"unsupported URL host: {parsed.netloc}")
        parts = [unquote(part) for part in parsed.path.split("/") if part]
        if len(parts) < 2 or parts[0] != "details":
            raise ConfigError("Internet Archive URLs must use /details/<identifier>")
        return parts[1]
    return value.strip()


def normalize_identifier(value: str) -> str:
    identifier = _details_identifier(value)
    if not _IDENTIFIER_RE.fullmatch(identifier):
        raise ConfigError(f"invalid Internet Archive identifier: {identifier!r}")
    return identifier


def normalize_favorites_owner(value: str) -> str:
    owner = _details_identifier(value)
    if owner.startswith("fav-"):
        owner = owner[4:]
    if not _IDENTIFIER_RE.fullmatch(owner):
        raise ConfigError(f"invalid favorites owner: {owner!r}")
    return owner


def default_source_path(name: str) -> str:
    value = re.sub(r"\s+", " ", name.strip())
    value = re.sub(r"[^A-Za-z0-9._ -]", "-", value)
    value = value.strip(" .-")
    if not value:
        raise ConfigError("source name cannot produce an empty directory name")
    return value


def validate_virtual_path(value: str) -> str:
    value = value.strip().replace("\\", "/")
    path = PurePosixPath(value)
    if not value or path.is_absolute() or ".." in path.parts:
        raise ConfigError(f"invalid virtual path: {value!r}")
    for component in path.parts:
        if component in {"", "."} or not _PATH_COMPONENT_RE.fullmatch(component):
            raise ConfigError(f"invalid virtual path component: {component!r}")
    return path.as_posix()


def load_config(path: Path) -> AppConfig:
    try:
        data = tomllib.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as exc:
        raise ConfigError(f"config file not found: {path}") from exc
    except tomllib.TOMLDecodeError as exc:
        raise ConfigError(f"invalid TOML in {path}: {exc}") from exc

    version = data.get("version")
    if version != 1:
        raise ConfigError("config version must be 1")

    raw_sources = data.get("sources")
    if not isinstance(raw_sources, list) or not raw_sources:
        raise ConfigError("at least one [[sources]] entry is required")

    sources: list[Source] = []
    seen_paths: set[str] = set()
    for index, raw in enumerate(raw_sources, start=1):
        if not isinstance(raw, dict):
            raise ConfigError(f"sources entry {index} must be a table")
        name = str(raw.get("name", "")).strip()
        kind = str(raw.get("kind", "")).strip().lower()
        value = str(raw.get("value", "")).strip()
        if not name or kind not in _ALLOWED_KINDS or not value:
            raise ConfigError(
                f"sources entry {index} requires name, value, and kind in {sorted(_ALLOWED_KINDS)}"
            )
        virtual_path = validate_virtual_path(str(raw.get("path") or default_source_path(name)))
        if virtual_path in seen_paths:
            raise ConfigError(f"duplicate source path: {virtual_path}")
        seen_paths.add(virtual_path)
        sources.append(Source(name=name, kind=kind, value=value, path=virtual_path))

    raw_serve = data.get("serve", {})
    if not isinstance(raw_serve, dict):
        raise ConfigError("[serve] must be a table")
    address = str(raw_serve.get("address", "127.0.0.1:8080")).strip()
    remote_name = str(raw_serve.get("remote_name", "archive-media-drive")).strip()
    if not address or not re.fullmatch(r"[A-Za-z0-9.:[\]-]+", address):
        raise ConfigError(f"invalid serve address: {address!r}")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", remote_name):
        raise ConfigError(f"invalid rclone remote name: {remote_name!r}")

    return AppConfig(
        sources=tuple(sources),
        serve=ServeConfig(address=address, remote_name=remote_name),
    )

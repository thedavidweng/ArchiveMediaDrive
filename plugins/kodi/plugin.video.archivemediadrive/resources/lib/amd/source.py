from __future__ import annotations

import json
import re
from dataclasses import dataclass, asdict
from urllib.parse import unquote, urlparse

_SCHEMA_VERSION = 1
_VALID_KINDS = ("item", "collection", "favorites", "search")
_IDENTIFIER_RE = re.compile(r"^[A-Za-z0-9._-]+$")
_ID_RE = re.compile(r"^[a-z0-9][a-z0-9-]{0,63}$")
_ALLOWED_HOSTS = {"archive.org", "www.archive.org"}


class SourceError(ValueError):
    pass


def _identifier_from_details_url(value: str) -> str:
    parsed = urlparse(value)
    if parsed.scheme and parsed.netloc:
        if parsed.netloc.lower() not in _ALLOWED_HOSTS:
            raise SourceError(f"unsupported URL host: {parsed.netloc}")
        parts = [unquote(part) for part in parsed.path.split("/") if part]
        if len(parts) < 2 or parts[0] != "details":
            raise SourceError("Internet Archive URLs must use /details/<identifier>")
        return parts[1]
    return value.strip()


def normalize_identifier(value: str) -> str:
    identifier = _identifier_from_details_url(value)
    if not _IDENTIFIER_RE.fullmatch(identifier):
        raise SourceError(f"invalid Internet Archive identifier: {identifier!r}")
    return identifier


def normalize_value(kind: str, value: str) -> str:
    if kind == "item":
        return normalize_identifier(value)
    if kind == "collection":
        return normalize_identifier(value)
    if kind == "favorites":
        owner = _identifier_from_details_url(value)
        if owner.startswith("fav-"):
            owner = owner[4:]
        if not _IDENTIFIER_RE.fullmatch(owner):
            raise SourceError(f"invalid favorites owner: {owner!r}")
        return owner
    return value.strip()


@dataclass
class Source:
    id: str
    name: str
    kind: str
    value: str
    enabled: bool = True
    refresh_minutes: int = 360
    authentication_ref: str | None = None
    schema_version: int = _SCHEMA_VERSION

    def __post_init__(self) -> None:
        if self.kind not in _VALID_KINDS:
            raise SourceError(f"invalid source kind: {self.kind!r}")
        if not _ID_RE.fullmatch(self.id):
            raise SourceError(f"invalid source id: {self.id!r}")
        if not self.name:
            raise SourceError("source name must not be empty")
        if not (5 <= self.refresh_minutes <= 10080):
            raise SourceError("refreshMinutes must be between 5 and 10080")
        self.value = normalize_value(self.kind, self.value)

    def to_dict(self) -> dict:
        return {
            "schemaVersion": self.schema_version,
            "id": self.id,
            "name": self.name,
            "kind": self.kind,
            "value": self.value,
            "enabled": self.enabled,
            "refreshMinutes": self.refresh_minutes,
            "authenticationRef": self.authentication_ref,
        }

    def to_json(self) -> str:
        return json.dumps(self.to_dict(), separators=(",", ":"), ensure_ascii=False)

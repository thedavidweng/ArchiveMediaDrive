from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Literal

SourceKind = Literal["item", "collection", "favorites", "search"]


@dataclass(frozen=True)
class Source:
    name: str
    kind: SourceKind
    value: str
    path: str


@dataclass(frozen=True)
class ServeConfig:
    address: str = "127.0.0.1:8080"
    remote_name: str = "archive-media-drive"


@dataclass(frozen=True)
class AppConfig:
    sources: tuple[Source, ...]
    serve: ServeConfig


@dataclass(frozen=True)
class Paths:
    state_dir: Path

    @property
    def catalog(self) -> Path:
        return self.state_dir / "catalog.json"

    @property
    def rclone_config(self) -> Path:
        return self.state_dir / "rclone.conf"

from __future__ import annotations

import sys
from dataclasses import dataclass
from pathlib import Path

_VENDOR_DIR = Path(__file__).resolve().parent.parent / "vendor"
if _VENDOR_DIR.is_dir() and str(_VENDOR_DIR) not in sys.path:
    sys.path.insert(0, str(_VENDOR_DIR))


@dataclass
class IaFile:
    name: str
    size: int
    format: str


@dataclass
class IaItem:
    identifier: str
    files: list[IaFile]


@dataclass
class IaSearchResult:
    identifier: str
    title: str


class IaClient:
    def __init__(self) -> None:
        import internetarchive as ia
        self._ia = ia

    def search(self, query: str):
        results = self._ia.search_items(query)
        for item in results:
            identifier = item.get("identifier", "")
            title = item.get("title", identifier)
            yield IaSearchResult(identifier=identifier, title=title)

    def get_item(self, identifier: str) -> IaItem:
        item = self._ia.get_item(identifier)
        files = [
            IaFile(
                name=f.get("name", ""),
                size=int(f.get("size", 0) or 0),
                format=f.get("format", ""),
            )
            for f in item.files
        ]
        return IaItem(identifier=identifier, files=files)


def create_client() -> IaClient:
    return IaClient()

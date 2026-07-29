import json
import unittest
from pathlib import Path

import xbmcstub

from resources.lib.amd.source import Source, normalize_value

_FIXTURES = Path(__file__).resolve().parents[3] / "contracts" / "fixtures" / "sources.json"


class SourceFixturesTests(unittest.TestCase):
    def setUp(self) -> None:
        self._data = json.loads(_FIXTURES.read_text(encoding="utf-8"))

    def test_all_fixture_sources_normalize_and_serialize(self) -> None:
        for fixture in self._data["sources"]:
            normalized = normalize_value(fixture["kind"], fixture["value"])
            source = Source(
                id=fixture["id"],
                name=fixture["name"],
                kind=fixture["kind"],
                value=normalized,
                enabled=fixture["enabled"],
                refresh_minutes=fixture["refreshMinutes"],
                authentication_ref=fixture["authenticationRef"],
            )
            payload = json.loads(source.to_json())
            self.assertEqual(payload["id"], fixture["id"])
            self.assertEqual(payload["value"], normalized)
            self.assertEqual(payload["kind"], fixture["kind"])

    def test_url_fixtures_collapse_to_identifiers(self) -> None:
        by_id = {s["id"]: s for s in self._data["sources"]}
        self.assertEqual(normalize_value("item", by_id["tripdown"]["value"]), "TripDown1905")
        self.assertEqual(normalize_value("collection", by_id["prelinger-url"]["value"]), "prelinger")
        self.assertEqual(normalize_value("favorites", by_id["david-favs"]["value"]), "david")


if __name__ == "__main__":
    unittest.main()

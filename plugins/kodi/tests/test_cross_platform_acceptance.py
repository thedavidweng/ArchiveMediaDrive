import json
import unittest
from pathlib import Path

import xbmcstub

from resources.lib.amd.source import Source, normalize_value

_FIXTURES = Path(__file__).resolve().parents[3] / "contracts" / "fixtures" / "sources.json"


class CrossPlatformAcceptanceTests(unittest.TestCase):
    def test_kodi_normalizes_shared_source_fixtures_identically_to_dotnet_contract(self) -> None:
        data = json.loads(_FIXTURES.read_text(encoding="utf-8"))
        for fixture in data["sources"]:
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
            self.assertEqual(payload["schemaVersion"], 1)
            self.assertEqual(payload["id"], fixture["id"])
            self.assertEqual(payload["kind"], fixture["kind"])
            self.assertEqual(payload["enabled"], fixture["enabled"])
            self.assertEqual(payload["refreshMinutes"], fixture["refreshMinutes"])

    def test_kodi_and_dotnet_produce_byte_identical_json_for_item_source(self) -> None:
        source = Source(
            schema_version=1,
            id="tripdown",
            name="Trip Down",
            kind="item",
            value="TripDown1905",
            enabled=True,
            refresh_minutes=360,
            authentication_ref=None,
        )
        expected = (
            '{"schemaVersion":1,"id":"tripdown","name":"Trip Down","kind":"item",'
            '"value":"TripDown1905","enabled":true,"refreshMinutes":360,"authenticationRef":null}'
        )
        self.assertEqual(source.to_json(), expected)

    def test_all_fixture_kinds_are_supported(self) -> None:
        data = json.loads(_FIXTURES.read_text(encoding="utf-8"))
        kinds = {s["kind"] for s in data["sources"]}
        self.assertEqual(kinds, {"item", "collection", "favorites", "search"})


if __name__ == "__main__":
    unittest.main()

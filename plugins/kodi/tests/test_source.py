import json
import unittest
from pathlib import Path

import xbmcstub

from resources.lib.amd.source import Source, SourceError, normalize_identifier, normalize_value


class SourceModelTests(unittest.TestCase):
    def test_item_source_serializes_byte_for_byte_like_dotnet(self) -> None:
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

    def test_all_kinds_serialize_as_lowercase_strings(self) -> None:
        for kind, expected in [
            ("collection", "collection"),
            ("favorites", "favorites"),
            ("search", "search"),
            ("item", "item"),
        ]:
            source = Source(id="s", name="S", kind=kind, value="v")
            payload = json.loads(source.to_json())
            self.assertEqual(payload["kind"], expected)

    def test_source_id_is_stable_across_display_name_changes(self) -> None:
        first = Source(id="prelinger", name="Prelinger", kind="collection", value="prelinger")
        renamed = Source(id="prelinger", name="Prelinger Films", kind="collection", value="prelinger")
        self.assertEqual(first.id, renamed.id)

    def test_normalize_identifier_extracts_details_url_identifier(self) -> None:
        self.assertEqual(
            normalize_identifier("https://archive.org/details/TripDown1905"),
            "TripDown1905",
        )

    def test_normalize_identifier_rejects_path_traversal(self) -> None:
        with self.assertRaises(SourceError):
            normalize_identifier("../secret")

    def test_normalize_value_collection(self) -> None:
        self.assertEqual(normalize_value("collection", "https://archive.org/details/prelinger"), "prelinger")

    def test_normalize_value_favorites_strips_fav_prefix(self) -> None:
        self.assertEqual(normalize_value("favorites", "fav-david"), "david")
        self.assertEqual(normalize_value("favorites", "david"), "david")

    def test_normalize_value_search_is_passthrough(self) -> None:
        self.assertEqual(normalize_value("search", "mediatype:movies"), "mediatype:movies")

    def test_invalid_kind_rejected(self) -> None:
        with self.assertRaises(SourceError):
            Source(id="s", name="S", kind="playlist", value="v")

    def test_invalid_id_rejected(self) -> None:
        with self.assertRaises(SourceError):
            Source(id="UPPER", name="S", kind="item", value="v")


if __name__ == "__main__":
    unittest.main()

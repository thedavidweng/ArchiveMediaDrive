import json
import subprocess
import unittest
from pathlib import Path

from archivemediadrive.fixtures import (
    FixtureError,
    capture_identifier,
    capture_search,
    sanitize_metadata,
)

_FIXTURES_DIR = Path(__file__).resolve().parents[3] / "contracts" / "fixtures" / "ia"
_LEAK_KEYS = ("server", "d1", "d2", "dir", "alternate_locations", "workable_servers", "md5", "sha1", "crc32")

_CANNED_METADATA = {
    "server": "dn600307.us.archive.org",
    "d1": "dn600307.us.archive.org",
    "dir": "/0/items/TripDown1905",
    "alternate_locations": {"servers": [], "workable": []},
    "workable_servers": [],
    "created": 1785283844,
    "files_count": 10,
    "item_size": 1234567,
    "item_last_updated": 1700000000,
    "uniq": 12345,
    "solo": True,
    "reviews": [],
    "simplelists": {},
    "metadata": {
        "identifier": "TripDown1905",
        "title": "Trip Down",
        "mediatype": "movies",
        "collection": "prelinger",
        "publicdate": "2020-01-01",
    },
    "files": [
        {"name": "TripDown1905.mp4", "source": "derivative", "format": "MPEG4", "size": "1000", "mtime": "1472365953", "md5": "x", "sha1": "y", "crc32": "z"},
        {"name": "thumbs/t1.jpg", "source": "derivative", "format": "Thumbnail", "size": "200", "mtime": "1472365954", "md5": "x", "sha1": "y", "crc32": "z"},
    ],
}

_CANNED_LIST = "TripDown1905.mp4\nthumbs/t1.jpg\n"


def _runner(*args, **kwargs):
    command = args[0]
    if "metadata" in command:
        return subprocess.CompletedProcess(args, 0, json.dumps(_CANNED_METADATA), "")
    if "list" in command:
        return subprocess.CompletedProcess(args, 0, _CANNED_LIST, "")
    if "search" in command:
        return subprocess.CompletedProcess(args, 0, "beta\nalpha\nbeta\n", "")
    return subprocess.CompletedProcess(args, 1, "", "unknown")


class SanitizeMetadataTests(unittest.TestCase):
    def test_strips_server_dir_and_hashes(self) -> None:
        sanitized = sanitize_metadata(_CANNED_METADATA)
        for key in ("server", "d1", "dir", "alternate_locations", "workable_servers", "reviews", "simplelists", "solo", "files_count", "item_size", "created"):
            self.assertNotIn(key, sanitized)
        for f in sanitized["files"]:
            for hash_name in ("md5", "sha1", "crc32"):
                self.assertNotIn(hash_name, f)

    def test_keeps_identifier_revision_and_file_fields(self) -> None:
        sanitized = sanitize_metadata(_CANNED_METADATA)
        self.assertEqual(sanitized["identifier"], "TripDown1905")
        self.assertEqual(sanitized["revision"], "12345")
        self.assertEqual(sanitized["mediatype"], "movies")
        self.assertEqual(len(sanitized["files"]), 2)
        first = sanitized["files"][0]
        self.assertEqual(first["name"], "TripDown1905.mp4")
        self.assertEqual(first["source"], "derivative")
        self.assertEqual(first["format"], "MPEG4")
        self.assertEqual(first["size"], 1000)


class CaptureIdentifierTests(unittest.TestCase):
    def test_returns_sanitized_fixture_with_ordered_files(self) -> None:
        fixture = capture_identifier("TripDown1905", runner=_runner)
        self.assertEqual(fixture["identifier"], "TripDown1905")
        self.assertEqual(fixture["files"][0]["name"], "TripDown1905.mp4")
        self.assertEqual(fixture["files"][1]["name"], "thumbs/t1.jpg")

    def test_missing_identifier_in_metadata_raises(self) -> None:
        def runner(*args, **kwargs):
            bad = json.loads(json.dumps(_CANNED_METADATA))
            bad["metadata"].pop("identifier")
            return subprocess.CompletedProcess(args, 0, json.dumps(bad), "")
        with self.assertRaises(FixtureError):
            capture_identifier("TripDown1905", runner=runner)


class CaptureSearchTests(unittest.TestCase):
    def test_preserves_order_and_deduplicates(self) -> None:
        result = capture_search("collection:prelinger", runner=_runner)
        self.assertEqual(result["identifiers"], ["beta", "alpha"])
        self.assertEqual(result["query"], "collection:prelinger")


class CommittedFixturesTests(unittest.TestCase):
    def test_committed_fixtures_contain_no_sensitive_fields(self) -> None:
        for path in _FIXTURES_DIR.glob("*.json"):
            if path.name == "manifest.json":
                continue
            text = path.read_text(encoding="utf-8")
            for key in _LEAK_KEYS:
                self.assertNotIn(f'"{key}"', text, f"{path.name} leaks {key}")

    def test_committed_item_fixtures_have_required_shape(self) -> None:
        for path in _FIXTURES_DIR.glob("item-*.json"):
            data = json.loads(path.read_text(encoding="utf-8"))
            if data.get("expectFailure"):
                continue
            self.assertEqual(data["schemaVersion"], 1)
            self.assertIn("identifier", data)
            self.assertIn("files", data)
            self.assertIsInstance(data["files"], list)


if __name__ == "__main__":
    unittest.main()

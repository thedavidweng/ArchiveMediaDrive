from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from archivemediadrive.config import ConfigError, load_config, normalize_identifier


class ConfigTests(unittest.TestCase):
    def test_normalizes_details_url(self) -> None:
        self.assertEqual(
            normalize_identifier("https://archive.org/details/TripDown1905"),
            "TripDown1905",
        )

    def test_rejects_path_traversal(self) -> None:
        with self.assertRaises(ConfigError):
            normalize_identifier("../secret")

    def test_loads_sources(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "config.toml"
            path.write_text(
                '''version = 1\n[[sources]]\nname = "One"\nkind = "item"\nvalue = "TripDown1905"\n''',
                encoding="utf-8",
            )
            config = load_config(path)
            self.assertEqual(config.sources[0].path, "One")


if __name__ == "__main__":
    unittest.main()

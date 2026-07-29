from __future__ import annotations

import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from archivemediadrive.ia import ResolvedSource
from archivemediadrive.model import Source
from archivemediadrive.rclone import RcloneError, render_config, webdav_command


class RcloneTests(unittest.TestCase):
    def test_render_preserves_all_item_files_by_mounting_item_root(self) -> None:
        source = Source("Favorites", "favorites", "david", "Favorites")
        text = render_config(
            (ResolvedSource(source, ("item-a", "item-b")),),
            library_remote="archive-media-drive",
        )
        self.assertIn('"Favorites/item-a=archive-media-drive-ia:item-a"', text)
        self.assertNotIn("filter", text.lower())

    def test_refuses_public_unauthenticated_bind(self) -> None:
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, {}, clear=True):
            with self.assertRaises(RcloneError):
                webdav_command(
                    config_path=Path(directory) / "rclone.conf",
                    remote_name="archive-media-drive",
                    address="0.0.0.0:8080",
                    allow_public=False,
                )


if __name__ == "__main__":
    unittest.main()

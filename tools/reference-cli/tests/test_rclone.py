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
        self.assertIn("[archive-media-drive-src-0]", text)
        self.assertIn('"item-a=archive-media-drive-ia:item-a"', text)
        self.assertIn('"Favorites=archive-media-drive-src-0:"', text)
        self.assertNotIn("filter", text.lower())

    def test_render_merges_sources_sharing_a_path(self) -> None:
        first = Source("Fav A", "favorites", "david", "Favorites")
        second = Source("Fav B", "favorites", "other", "Favorites")
        text = render_config(
            (
                ResolvedSource(first, ("item-a",)),
                ResolvedSource(second, ("item-b",)),
            ),
            library_remote="archive-media-drive",
        )
        self.assertEqual(text.count("type = combine"), 2)
        self.assertIn('"item-a=archive-media-drive-ia:item-a"', text)
        self.assertIn('"item-b=archive-media-drive-ia:item-b"', text)
        self.assertIn('"Favorites=archive-media-drive-src-0:"', text)

    def test_render_rejects_duplicate_virtual_paths(self) -> None:
        source = Source("Favorites", "favorites", "david", "Favorites")
        with self.assertRaises(RcloneError):
            render_config(
                (
                    ResolvedSource(source, ("item-a",)),
                    ResolvedSource(source, ("item-a",)),
                ),
                library_remote="archive-media-drive",
            )

    def test_render_rejects_catalog_above_library_item_limit(self) -> None:
        source = Source("Favorites", "favorites", "david", "Favorites")
        identifiers = tuple(f"item-{i:04d}" for i in range(201))
        with self.assertRaises(RcloneError) as ctx:
            render_config(
                (ResolvedSource(source, identifiers),),
                library_remote="archive-media-drive",
            )
        self.assertIn("200", str(ctx.exception))

    def test_render_accepts_catalog_at_library_item_limit(self) -> None:
        source = Source("Favorites", "favorites", "david", "Favorites")
        identifiers = tuple(f"item-{i:04d}" for i in range(200))
        text = render_config(
            (ResolvedSource(source, identifiers),),
            library_remote="archive-media-drive",
        )
        self.assertIn('"item-0199=archive-media-drive-ia:item-0199"', text)

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

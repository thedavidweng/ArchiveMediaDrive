from __future__ import annotations

import shutil
import tempfile
import unittest
from pathlib import Path

from archivemediadrive.ia import ResolvedSource
from archivemediadrive.model import Source
from archivemediadrive.rclone import render_config

RCLONE = shutil.which("rclone")


@unittest.skipUnless(RCLONE, "rclone binary not available")
class RcloneRenderEndToEndTests(unittest.TestCase):
    def test_rendered_config_serves_source_and_item_tree(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            items = root / "items"
            for identifier in ("item-a", "item-b"):
                item_dir = items / identifier
                item_dir.mkdir(parents=True)
                (item_dir / "movie.mp4").write_bytes(b"fake")

            config_path = root / "rclone.conf"
            source = Source("Favorites", "favorites", "david", "Favorites")
            resolved = (ResolvedSource(source, ("item-a", "item-b")),)
            config_path.write_text(
                render_config(
                    resolved,
                    library_remote="amd-e2e",
                    ia_remote="amd-e2e-items",
                )
                    .replace(
                        "[amd-e2e-items]\ntype = internetarchive\n",
                        f"[amd-e2e-items]\ntype = alias\nremote = {items}\n",
                    ),
                encoding="utf-8",
            )

            top = self._lsf(config_path, "amd-e2e:")
            self.assertEqual(top, ["Favorites"])

            listed = self._lsf(config_path, "amd-e2e:Favorites")
            self.assertEqual(listed, ["item-a", "item-b"])

            files = self._lsf(config_path, "amd-e2e:Favorites/item-a")
            self.assertEqual(files, ["movie.mp4"])

    def _lsf(self, config_path: Path, remote: str) -> list[str]:
        import subprocess

        result = subprocess.run(
            [RCLONE, "lsf", remote, "--config", str(config_path)],
            check=True,
            capture_output=True,
            text=True,
        )
        return sorted(line.rstrip("/") for line in result.stdout.splitlines() if line)


if __name__ == "__main__":
    unittest.main()

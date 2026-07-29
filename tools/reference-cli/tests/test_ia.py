from __future__ import annotations

import subprocess
import unittest

from archivemediadrive.ia import resolve_source, source_query
from archivemediadrive.model import Source


class IATests(unittest.TestCase):
    def test_collection_query(self) -> None:
        source = Source("Collection", "collection", "prelinger", "Collection")
        self.assertEqual(source_query(source), "collection:prelinger")

    def test_favorites_query(self) -> None:
        source = Source("Favorites", "favorites", "fav-david", "Favorites")
        self.assertEqual(source_query(source), "collection:fav-david")

    def test_search_results_preserve_order_and_deduplicate(self) -> None:
        def runner(*args, **kwargs):
            return subprocess.CompletedProcess(args[0], 0, "zeta\nalpha\nzeta\n", "")

        source = Source("Search", "search", "mediatype:movies", "Search")
        result = resolve_source(source, runner=runner)
        self.assertEqual(result.identifiers, ("zeta", "alpha"))


if __name__ == "__main__":
    unittest.main()

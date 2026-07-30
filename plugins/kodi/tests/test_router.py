import json
import shutil
import tempfile
import unittest
from unittest.mock import MagicMock, patch
from urllib.parse import parse_qs, urlparse

import xbmcstub

from resources.lib.amd.router import build_plugin_url, run_with_handles


class RouterTests(unittest.TestCase):
    def setUp(self) -> None:
        self._xbmcgui = MagicMock()
        self._xbmcplugin = MagicMock()
        self._xbmcaddon = MagicMock()
        self._modules = {
            "xbmcgui": self._xbmcgui,
            "xbmcplugin": self._xbmcplugin,
            "xbmcaddon": self._xbmcaddon,
        }
        self._xbmcgui.Dialog.return_value.select.return_value = -1
        self._xbmcgui.Dialog.return_value.input.return_value = ""
        self._xbmcgui.Dialog.return_value.yesno.return_value = False
        self._xbmcgui.DialogProgress.return_value.iscanceled.return_value = False

    def _run(self, route: str, extra_params: dict | None = None, sources=None, ia_items=None) -> None:
        params = {"route": route}
        if extra_params:
            params.update(extra_params)
        query = "&".join(f"{k}={v}" for k, v in params.items())
        handle = 0
        argv = ["plugin://plugin.video.archivemediadrive/", str(handle), f"?{query}"]

        tmpdir = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, tmpdir)

        addon = MagicMock()
        addon.getSettingString.return_value = json.dumps(sources or [])
        addon.getAddonInfo.return_value = tmpdir
        self._xbmcaddon.Addon.return_value = addon

        list_items = []

        def make_list_item(label):
            li = MagicMock()
            li.label = label
            li._props = {}
            li.setPath = MagicMock()
            li.setProperty = MagicMock(side_effect=lambda k, v: li._props.__setitem__(k, v))
            list_items.append(li)
            return li

        self._xbmcgui.ListItem.side_effect = make_list_item
        self._xbmcplugin.addDirectoryItem.side_effect = lambda h, url, item, is_dir: list_items.append((url, item, is_dir))

        with patch.dict("sys.modules", self._modules):
            run_with_handles(argv, ia_client=_make_ia_client(ia_items or {}))

        self._list_items = list_items

    def test_root_lists_configured_sources_as_folders(self) -> None:
        sources = [
            {"id": "prelinger", "name": "Prelinger", "kind": "collection", "value": "prelinger"},
            {"id": "tripdown", "name": "Trip Down", "kind": "item", "value": "TripDown1905"},
        ]
        self._run("root", sources=sources)

        self._xbmcplugin.endOfDirectory.assert_called_once()
        calls = self._xbmcplugin.addDirectoryItem.call_args_list
        self.assertEqual(len(calls), 3)
        labels = [c.args[2].label for c in calls]
        self.assertIn("Prelinger", labels)
        self.assertIn("Trip Down", labels)
        self.assertIn("Manage sources", labels)

    def test_source_route_lists_items_from_ia_search(self) -> None:
        sources = [{"id": "prelinger", "name": "Prelinger", "kind": "collection", "value": "prelinger"}]
        ia_items = {
            "search:collection:prelinger": [
                ("alpha", "Alpha Film"),
                ("beta", "Beta Film"),
            ]
        }
        self._run("source", extra_params={"source_id": "prelinger"}, sources=sources, ia_items=ia_items)

        calls = self._xbmcplugin.addDirectoryItem.call_args_list
        self.assertEqual(len(calls), 2)
        urls = [c.args[1] for c in calls]
        for url in urls:
            parsed = urlparse(url)
            qs = parse_qs(parsed.query)
            self.assertEqual(qs["route"], ["item"])

    def test_item_route_filters_non_playable_files(self) -> None:
        ia_items = {
            "item:TripDown1905": [
                {"name": "TripDown1905.mp4", "size": 1000, "format": "MPEG4"},
                {"name": "TripDown1905.srt", "size": 200, "format": "SubRip"},
                {"name": "thumbs", "size": 0, "format": "Directory"},
            ]
        }
        self._run("item", extra_params={"identifier": "TripDown1905"}, ia_items=ia_items)

        calls = self._xbmcplugin.addDirectoryItem.call_args_list
        self.assertEqual(len(calls), 2)
        labels = [c.args[2].label for c in calls]
        self.assertIn("TripDown1905.mp4", labels)
        self.assertIn("thumbs", labels)
        self.assertNotIn("TripDown1905.srt", labels)

    def test_play_route_resolves_to_archive_download_url(self) -> None:
        ia_items = {
            "item:TripDown1905": [
                {"name": "TripDown1905.mp4", "size": 1000, "format": "MPEG4"},
            ]
        }
        self._run("play", extra_params={"identifier": "TripDown1905", "file": "TripDown1905.mp4"}, ia_items=ia_items)

        self._xbmcplugin.setResolvedUrl.assert_called_once()
        call = self._xbmcplugin.setResolvedUrl.call_args
        item = call.args[2]
        item.setPath.assert_called_with("https://archive.org/download/TripDown1905/TripDown1905.mp4")

    def test_nested_paths_build_directory_tree_at_root(self) -> None:
        ia_items = {
            "item:NestedItem": [
                {"name": "video/main.mkv", "size": 5000, "format": "Matroska"},
                {"name": "video/sub/trailer.mp4", "size": 1000, "format": "MPEG4"},
                {"name": "thumbs/image.jpg", "size": 200, "format": "JPEG"},
                {"name": "readme.txt", "size": 10, "format": "Text"},
            ]
        }
        self._run("item", extra_params={"identifier": "NestedItem"}, ia_items=ia_items)

        calls = self._xbmcplugin.addDirectoryItem.call_args_list
        labels = [c.args[2].label for c in calls]
        self.assertIn("video", labels)
        self.assertIn("thumbs", labels)
        self.assertNotIn("readme.txt", labels)
        self.assertNotIn("main.mkv", labels)
        self.assertNotIn("image.jpg", labels)

    def test_nested_paths_drills_into_subdirectory(self) -> None:
        ia_items = {
            "item:NestedItem": [
                {"name": "video/main.mkv", "size": 5000, "format": "Matroska"},
                {"name": "video/sub/trailer.mp4", "size": 1000, "format": "MPEG4"},
                {"name": "thumbs/image.jpg", "size": 200, "format": "JPEG"},
            ]
        }
        self._run("item", extra_params={"identifier": "NestedItem", "path": "video"}, ia_items=ia_items)

        calls = self._xbmcplugin.addDirectoryItem.call_args_list
        labels = [c.args[2].label for c in calls]
        self.assertIn("..", labels)
        self.assertIn("main.mkv", labels)
        self.assertIn("sub", labels)
        self.assertNotIn("thumbs", labels)
        self.assertNotIn("image.jpg", labels)


class BuildPluginUrlTests(unittest.TestCase):
    def test_builds_url_with_route_and_params(self) -> None:
        url = build_plugin_url("item", {"identifier": "TripDown1905", "file": "x.mp4"})
        parsed = urlparse(url)
        self.assertEqual(parsed.scheme, "plugin")
        qs = parse_qs(parsed.query)
        self.assertEqual(qs["route"], ["item"])
        self.assertEqual(qs["identifier"], ["TripDown1905"])


def _make_ia_client(items: dict):
    class FakeIaClient:
        def search(self, query):
            key = f"search:{query}"
            for identifier, title in items.get(key, []):
                yield MagicMock(identifier=identifier, title=title)

        def get_item(self, identifier):
            key = f"item:{identifier}"
            files = items.get(key, [])
            item = MagicMock()
            item.identifier = identifier
            file_mocks = []
            for f in files:
                m = MagicMock()
                m.name = f["name"]
                m.size = f.get("size", 0)
                m.format = f.get("format", "")
                file_mocks.append(m)
            item.files = file_mocks
            return item

    return FakeIaClient()


if __name__ == "__main__":
    unittest.main()

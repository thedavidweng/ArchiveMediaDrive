"""Kodi routing scaffold.

Release packaging must vendor the official ``internetarchive`` Python package
under resources/lib/vendor. The coding agent should complete the routes in the
implementation plan and preserve every file returned by Item.files.
"""
from __future__ import annotations

import json
import sys
from urllib.parse import parse_qs


def _load_sources(addon):
    raw = addon.getSettingString("sources_json") or "[]"
    value = json.loads(raw)
    if not isinstance(value, list):
        raise ValueError("sources_json must contain a JSON array")
    return value


def run() -> None:
    try:
        import xbmcaddon
        import xbmcgui
        import xbmcplugin
    except ImportError as exc:  # pragma: no cover - Kodi provides these modules
        raise RuntimeError("This entry point must run inside Kodi") from exc

    handle = int(sys.argv[1])
    params = parse_qs(sys.argv[2][1:] if len(sys.argv) > 2 and sys.argv[2].startswith("?") else "")
    addon = xbmcaddon.Addon()
    route = params.get("route", ["root"])[0]

    if route == "root":
        for source in _load_sources(addon):
            label = source.get("name") or source.get("id") or "Internet Archive"
            item = xbmcgui.ListItem(label=label)
            # TODO: build plugin URL and add source folder.
            xbmcplugin.addDirectoryItem(handle, "plugin://plugin.video.archivemediadrive/?route=source", item, True)
        xbmcplugin.endOfDirectory(handle)
        return

    xbmcgui.Dialog().notification("ArchiveMediaDrive", "Provider route is scaffolded; see AGENT_IMPLEMENTATION_PLAN.md")
    xbmcplugin.endOfDirectory(handle, succeeded=False)

from __future__ import annotations

import json
import sys
from urllib.parse import parse_qs, quote, urlencode

from .source import Source

_PLAYABLE_EXTENSIONS = {
    ".mp4", ".mkv", ".avi", ".mov", ".webm", ".ogv", ".mpeg", ".mpg", ".m4v",
    ".mp3", ".flac", ".ogg", ".oga", ".wav", ".m4a", ".aac", ".opus", ".weba",
}

_BASE_URL = "plugin://plugin.video.archivemediadrive/"


def build_plugin_url(route: str, params: dict | None = None) -> str:
    query = {"route": route}
    if params:
        query.update(params)
    return f"{_BASE_URL}?{urlencode(query)}"


def _load_sources(addon) -> list[dict]:
    raw = addon.getSettingString("sources_json") or "[]"
    value = json.loads(raw)
    if not isinstance(value, list):
        raise ValueError("sources_json must contain a JSON array")
    return value


def _is_playable(filename: str) -> bool:
    lower = filename.lower()
    return any(lower.endswith(ext) for ext in _PLAYABLE_EXTENSIONS)


def _archive_download_url(identifier: str, filename: str) -> str:
    return f"https://archive.org/download/{identifier}/{quote(filename)}"


def _source_query(source: dict) -> str:
    kind = source.get("kind", "")
    value = source.get("value", "")
    if kind == "collection":
        return f"collection:{value}"
    if kind == "favorites":
        owner = value[4:] if value.startswith("fav-") else value
        return f"collection:fav-{owner}"
    return value


def run_with_handles(argv, *, ia_client=None) -> None:
    import xbmcaddon
    import xbmcgui
    import xbmcplugin

    handle = int(argv[3]) if len(argv) > 3 else int(argv[1])
    query_string = argv[1] if len(argv) > 1 and argv[1].startswith("?") else ""
    params = {k: v[0] for k, v in parse_qs(query_string.lstrip("?")).items()}
    addon = xbmcaddon.Addon()
    route = params.get("route", "root")

    if route == "root":
        _render_root(handle, addon, xbmcgui, xbmcplugin)
        return

    if route == "source":
        _render_source(handle, params, addon, xbmcgui, xbmcplugin, ia_client)
        return

    if route == "item":
        _render_item(handle, params, xbmcgui, xbmcplugin, ia_client)
        return

    if route == "play":
        _resolve_play(handle, params, xbmcgui, xbmcplugin, ia_client)
        return

    xbmcplugin.endOfDirectory(handle, succeeded=False)


def _render_root(handle, addon, xbmcgui, xbmcplugin) -> None:
    for source in _load_sources(addon):
        label = source.get("name") or source.get("id") or "Internet Archive"
        item = xbmcgui.ListItem(label=label)
        url = build_plugin_url("source", {"source_id": source["id"]})
        xbmcplugin.addDirectoryItem(handle, url, item, True)
    xbmcplugin.endOfDirectory(handle)


def _render_source(handle, params, addon, xbmcgui, xbmcplugin, ia_client) -> None:
    sources = _load_sources(addon)
    source = next((s for s in sources if s["id"] == params.get("source_id")), None)
    if source is None:
        xbmcplugin.endOfDirectory(handle, succeeded=False)
        return

    if source["kind"] == "item":
        identifier = source["value"]
        url = build_plugin_url("item", {"identifier": identifier})
        item = xbmcgui.ListItem(label=source.get("name") or identifier)
        xbmcplugin.addDirectoryItem(handle, url, item, True)
        xbmcplugin.endOfDirectory(handle)
        return

    query = _source_query(source)
    for result in ia_client.search(query):
        identifier = result.identifier
        title = getattr(result, "title", None) or identifier
        url = build_plugin_url("item", {"identifier": identifier})
        item = xbmcgui.ListItem(label=title)
        xbmcplugin.addDirectoryItem(handle, url, item, True)
    xbmcplugin.endOfDirectory(handle)


def _render_item(handle, params, xbmcgui, xbmcplugin, ia_client) -> None:
    identifier = params.get("identifier", "")
    if not identifier:
        xbmcplugin.endOfDirectory(handle, succeeded=False)
        return

    item = ia_client.get_item(identifier)
    for f in item.files:
        name = f.name
        is_dir = (f.format or "").lower() in ("directory",) or name.endswith("/")
        if is_dir:
            url = build_plugin_url("item", {"identifier": identifier, "path": name})
            li = xbmcgui.ListItem(label=name)
            xbmcplugin.addDirectoryItem(handle, url, li, True)
        elif _is_playable(name):
            url = build_plugin_url("play", {"identifier": identifier, "file": name})
            li = xbmcgui.ListItem(label=name)
            li.setProperty("isPlayable", "true")
            li.setPath(url)
            xbmcplugin.addDirectoryItem(handle, url, li, False)
        else:
            li = xbmcgui.ListItem(label=name)
            xbmcplugin.addDirectoryItem(handle, "", li, False)
    xbmcplugin.endOfDirectory(handle)


def _resolve_play(handle, params, xbmcgui, xbmcplugin, ia_client) -> None:
    identifier = params.get("identifier", "")
    filename = params.get("file", "")
    if not identifier or not filename:
        xbmcplugin.endOfDirectory(handle, succeeded=False)
        return

    download_url = _archive_download_url(identifier, filename)
    item = xbmcgui.ListItem(label=filename)
    item.setPath(download_url)
    xbmcplugin.setResolvedUrl(handle, True, item)


def run() -> None:
    try:
        import xbmcaddon
        import xbmcgui
        import xbmcplugin
    except ImportError as exc:
        raise RuntimeError("This entry point must run inside Kodi") from exc

    from .ia_client import create_client
    argv = list(sys.argv)
    run_with_handles(argv, ia_client=create_client())

from __future__ import annotations

import re
import sys
import time
import unicodedata
from urllib.parse import parse_qs, quote, urlencode

from .cache import load_cache, save_cache
from .settings import load_sources, save_sources
from .source import Source, SourceError

_BASE_URL = "plugin://plugin.video.archivemediadrive/"
_PAGE_SIZE_SOURCE = 25
_PAGE_SIZE_ITEM = 50


def build_plugin_url(route: str, params: dict | None = None) -> str:
    query = {"route": route}
    if params:
        query.update(params)
    return f"{_BASE_URL}?{urlencode(query)}"


def _files_to_dicts(files) -> list[dict]:
    return [{"name": f.name, "size": f.size, "format": f.format} for f in files]


def _dicts_to_files(dicts) -> list:
    from .ia_client import IaFile

    return [IaFile(name=f["name"], size=f["size"], format=f["format"]) for f in dicts]


def _archive_download_url(identifier: str, filename: str) -> str:
    return f"https://archive.org/download/{identifier}/{quote(filename, safe='/')}"


def _source_query(source: dict) -> str:
    kind = source.get("kind", "")
    value = source.get("value", "")
    if kind == "collection":
        return f"collection:{value}"
    if kind == "favorites":
        owner = value[4:] if value.startswith("fav-") else value
        return f"collection:fav-{owner}"
    return value


def _suggest_id(name: str) -> str:
    normalized = "".join(
        c if c.isalnum() or c in (" ", "-") else "-"
        for c in unicodedata.normalize("NFKD", name)
    )
    normalized = re.sub(r"\s+", "-", normalized.strip().lower())
    normalized = re.sub(r"-+", "-", normalized).strip("-")
    if not normalized:
        normalized = "source"
    if not re.match(r"^[a-z0-9]", normalized):
        normalized = "source-" + normalized
    return normalized[:64]


def _notify(xbmcgui, message: str) -> None:
    xbmcgui.Dialog().notification("ArchiveMediaDrive", message)


def _resolve_source(addon, ia_client, source: dict, xbmcgui, force: bool = False) -> dict:
    refresh_minutes = source.get("refreshMinutes", source.get("refresh_minutes", 360))
    if not force:
        cached = load_cache(addon, source["id"], refresh_minutes)
        if cached:
            return cached

    if source["kind"] == "item":
        item = ia_client.get_item(source["value"])
        files = item.files
        data = {
            "resolvedAt": int(time.time()),
            "source": source,
            "results": [{"identifier": source["value"], "title": source["name"]}],
            "items": {source["value"]: _files_to_dicts(files)},
        }
    else:
        query = _source_query(source)
        progress = xbmcgui.DialogProgress()
        progress.create("ArchiveMediaDrive", f"Loading {source['name']}")
        results = []
        try:
            for i, result in enumerate(ia_client.search(query)):
                if progress.iscanceled():
                    break
                if i % 100 == 0:
                    progress.update(0, f"{i} results")
                title = getattr(result, "title", None) or result.identifier
                results.append({"identifier": result.identifier, "title": title})
        finally:
            progress.close()

        data = {
            "resolvedAt": int(time.time()),
            "source": source,
            "results": results,
            "items": {},
        }

    save_cache(addon, source["id"], data)
    return data


def _get_item_files(addon, ia_client, identifier: str, source_id: str):
    if source_id:
        sources = load_sources(addon)
        source = next((s for s in sources if s.get("id") == source_id), None)
        if source:
            refresh_minutes = source.get("refreshMinutes", source.get("refresh_minutes", 360))
            cached = load_cache(addon, source_id, refresh_minutes)
            if cached and identifier in cached.get("items", {}):
                return _dicts_to_files(cached["items"][identifier])
            try:
                item = ia_client.get_item(identifier)
                files = item.files
                if cached is None:
                    cached = {"resolvedAt": int(time.time()), "source": source, "results": [], "items": {}}
                cached["resolvedAt"] = int(time.time())
                cached["items"][identifier] = _files_to_dicts(files)
                save_cache(addon, source_id, cached)
                return files
            except Exception:
                pass

    item = ia_client.get_item(identifier)
    return item.files


def _collect_entries(files, current_path: str) -> list[tuple[str, bool, str]]:
    prefix = f"{current_path}/" if current_path else ""
    dirs: dict[str, str] = {}
    file_entries: list[tuple[str, str]] = []

    for f in files:
        name = f.name
        is_format_dir = (f.format or "").lower() in ("directory",) or name.endswith("/")
        if is_format_dir:
            rel = name.rstrip("/")
        else:
            rel = name

        if prefix and not rel.startswith(prefix):
            continue
        remainder = rel[len(prefix):]
        if not remainder:
            continue

        slash_idx = remainder.find("/")
        if slash_idx >= 0:
            dir_name = remainder[:slash_idx]
            if dir_name not in dirs:
                dirs[dir_name] = f"{prefix}{dir_name}"
        elif is_format_dir:
            dirs[remainder] = rel
        else:
            file_entries.append((remainder, rel))

    result = [(name, True, path) for name, path in sorted(dirs.items())]
    result += [(name, False, rel) for name, rel in sorted(file_entries)]
    return result


def run_with_handles(argv, *, ia_client=None) -> None:
    import xbmcaddon
    import xbmcgui
    import xbmcplugin

    handle = int(argv[1]) if len(argv) > 1 else 0
    query_string = argv[2] if len(argv) > 2 else ""
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
        _render_item(handle, params, addon, xbmcgui, xbmcplugin, ia_client)
        return

    if route == "play":
        _resolve_play(handle, params, xbmcgui, xbmcplugin, ia_client)
        return

    if route == "settings":
        _settings(handle, addon, xbmcgui, xbmcplugin, ia_client)
        return

    if route == "add_source":
        _add_source(addon, xbmcgui)
        xbmcplugin.endOfDirectory(handle)
        return

    if route == "edit_source":
        _edit_source(addon, xbmcgui, params)
        xbmcplugin.endOfDirectory(handle)
        return

    if route == "delete_source":
        _delete_source(addon, xbmcgui, params)
        xbmcplugin.endOfDirectory(handle)
        return

    if route == "test_source":
        _test_source(addon, xbmcgui, ia_client, params)
        xbmcplugin.endOfDirectory(handle)
        return

    if route == "refresh_source":
        _refresh_source(addon, xbmcgui, ia_client, params)
        xbmcplugin.endOfDirectory(handle)
        return

    xbmcplugin.endOfDirectory(handle, succeeded=False)


def _render_root(handle, addon, xbmcgui, xbmcplugin) -> None:
    for source in load_sources(addon):
        label = source.get("name") or source.get("id") or "Internet Archive"
        item = xbmcgui.ListItem(label=label)
        url = build_plugin_url("source", {"source_id": source["id"]})
        xbmcplugin.addDirectoryItem(handle, url, item, True)

    item = xbmcgui.ListItem(label="Manage sources")
    xbmcplugin.addDirectoryItem(handle, build_plugin_url("settings"), item, True)
    xbmcplugin.endOfDirectory(handle)


def _render_source(handle, params, addon, xbmcgui, xbmcplugin, ia_client) -> None:
    sources = load_sources(addon)
    source = next((s for s in sources if s.get("id") == params.get("source_id")), None)
    if source is None:
        xbmcplugin.endOfDirectory(handle, succeeded=False)
        return

    if source["kind"] == "item":
        data = _resolve_source(addon, ia_client, source, xbmcgui)
        files = _dicts_to_files(data["items"].get(source["value"], []))
        _render_item(
            handle,
            {"identifier": source["value"], "source_id": source["id"]},
            addon,
            xbmcgui,
            xbmcplugin,
            ia_client,
            files=files,
        )
        return

    data = _resolve_source(addon, ia_client, source, xbmcgui)
    results = data.get("results", [])
    page = int(params.get("page", 0))
    start = page * _PAGE_SIZE_SOURCE
    end = start + _PAGE_SIZE_SOURCE

    if page > 0:
        item = xbmcgui.ListItem(label="Previous page")
        url = build_plugin_url("source", {"source_id": source["id"], "page": page - 1})
        xbmcplugin.addDirectoryItem(handle, url, item, True)

    for result in results[start:end]:
        identifier = result["identifier"]
        title = result.get("title") or identifier
        url = build_plugin_url("item", {"identifier": identifier, "source_id": source["id"]})
        item = xbmcgui.ListItem(label=title)
        xbmcplugin.addDirectoryItem(handle, url, item, True)

    if len(results) > end:
        item = xbmcgui.ListItem(label="Next page")
        url = build_plugin_url("source", {"source_id": source["id"], "page": page + 1})
        xbmcplugin.addDirectoryItem(handle, url, item, True)

    xbmcplugin.endOfDirectory(handle)


def _render_item(handle, params, addon, xbmcgui, xbmcplugin, ia_client, files=None) -> None:
    identifier = params.get("identifier", "")
    if not identifier:
        xbmcplugin.endOfDirectory(handle, succeeded=False)
        return

    current_path = params.get("path", "").rstrip("/")
    source_id = params.get("source_id", "")

    if files is None:
        files = _get_item_files(addon, ia_client, identifier, source_id)

    entries = _collect_entries(files, current_path)
    page = int(params.get("page", 0))
    start = page * _PAGE_SIZE_ITEM
    end = start + _PAGE_SIZE_ITEM

    if current_path:
        parent = "/".join(current_path.split("/")[:-1])
        parent_params = {"identifier": identifier, "source_id": source_id}
        if parent:
            parent_params["path"] = parent
        li = xbmcgui.ListItem(label="..")
        xbmcplugin.addDirectoryItem(handle, build_plugin_url("item", parent_params), li, True)

    if page > 0:
        prev_params = {"identifier": identifier, "source_id": source_id}
        if current_path:
            prev_params["path"] = current_path
        prev_params["page"] = page - 1
        li = xbmcgui.ListItem(label="Previous page")
        xbmcplugin.addDirectoryItem(handle, build_plugin_url("item", prev_params), li, True)

    for name, is_dir, full_path in entries[start:end]:
        if is_dir:
            url = build_plugin_url("item", {"identifier": identifier, "source_id": source_id, "path": full_path})
            li = xbmcgui.ListItem(label=name)
            xbmcplugin.addDirectoryItem(handle, url, li, True)
        else:
            url = build_plugin_url("play", {"identifier": identifier, "source_id": source_id, "file": full_path})
            li = xbmcgui.ListItem(label=name)
            li.setProperty("isPlayable", "true")
            li.setPath(url)
            xbmcplugin.addDirectoryItem(handle, url, li, False)

    if len(entries) > end:
        next_params = {"identifier": identifier, "source_id": source_id}
        if current_path:
            next_params["path"] = current_path
        next_params["page"] = page + 1
        li = xbmcgui.ListItem(label="Next page")
        xbmcplugin.addDirectoryItem(handle, build_plugin_url("item", next_params), li, True)

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


def _settings(handle, addon, xbmcgui, xbmcplugin, ia_client) -> None:
    sources = load_sources(addon)
    options = ["Add source"] + [f"{s.get('name')} ({s.get('kind')})" for s in sources]
    idx = xbmcgui.Dialog().select("ArchiveMediaDrive", options)
    if idx == 0:
        _add_source(addon, xbmcgui)
    elif idx > 0:
        _source_menu(addon, xbmcgui, sources[idx - 1], ia_client)
    xbmcplugin.endOfDirectory(handle)


def _source_menu(addon, xbmcgui, source, ia_client) -> None:
    options = ["Edit", "Delete", "Test", "Refresh"]
    idx = xbmcgui.Dialog().select(source.get("name", ""), options)
    if idx == 0:
        _edit_source(addon, xbmcgui, source)
    elif idx == 1:
        _delete_source(addon, xbmcgui, source)
    elif idx == 2:
        _test_source(addon, xbmcgui, ia_client, source)
    elif idx == 3:
        _refresh_source(addon, xbmcgui, ia_client, source)


def _add_source(addon, xbmcgui) -> None:
    dialog = xbmcgui.Dialog()
    name = dialog.input("Source name")
    if not name:
        return
    suggested = _suggest_id(name)
    source_id = dialog.input("Source ID", defaultvalue=suggested)
    if not source_id:
        return
    kinds = ["item", "collection", "favorites", "search"]
    kind_idx = dialog.select("Source kind", kinds)
    if kind_idx < 0:
        return
    value = dialog.input("Source value")
    if not value:
        return
    refresh = dialog.input("Refresh interval (minutes)", defaultvalue="360")
    try:
        refresh = int(refresh)
    except ValueError:
        _notify(xbmcgui, "Refresh interval must be a number")
        return
    try:
        source = Source(id=source_id, name=name, kind=kinds[kind_idx], value=value, refresh_minutes=refresh)
    except SourceError as exc:
        _notify(xbmcgui, str(exc))
        return
    sources = load_sources(addon)
    sources.append(source.to_dict())
    save_sources(addon, sources)
    _notify(xbmcgui, f"Added {source.name}")


def _edit_source(addon, xbmcgui, params) -> None:
    source_id = params.get("source_id", "")
    source = next((s for s in load_sources(addon) if s.get("id") == source_id), None)
    if source is None:
        _notify(xbmcgui, "Source not found")
        return

    dialog = xbmcgui.Dialog()
    name = dialog.input("Source name", defaultvalue=source.get("name", ""))
    if not name:
        return
    kinds = ["item", "collection", "favorites", "search"]
    current_kind = source.get("kind", "item")
    kind_idx = dialog.select("Source kind", kinds, preselect=kinds.index(current_kind))
    if kind_idx < 0:
        return
    value = dialog.input("Source value", defaultvalue=source.get("value", ""))
    if not value:
        return
    refresh = dialog.input(
        "Refresh interval (minutes)",
        defaultvalue=str(source.get("refreshMinutes", source.get("refresh_minutes", 360))),
    )
    try:
        refresh = int(refresh)
    except ValueError:
        _notify(xbmcgui, "Refresh interval must be a number")
        return
    auth = source.get("authenticationRef", source.get("authentication_ref"))
    enabled = source.get("enabled", True)
    try:
        new = Source(
            id=source_id,
            name=name,
            kind=kinds[kind_idx],
            value=value,
            enabled=enabled,
            refresh_minutes=refresh,
            authentication_ref=auth,
        )
    except SourceError as exc:
        _notify(xbmcgui, str(exc))
        return
    sources = [s for s in load_sources(addon) if s.get("id") != source_id]
    sources.append(new.to_dict())
    save_sources(addon, sources)
    _notify(xbmcgui, f"Updated {new.name}")


def _delete_source(addon, xbmcgui, params) -> None:
    source_id = params.get("source_id", "")
    source = next((s for s in load_sources(addon) if s.get("id") == source_id), None)
    if source is None:
        return
    if not xbmcgui.Dialog().yesno("ArchiveMediaDrive", f"Delete {source.get('name')}?"):
        return
    sources = [s for s in load_sources(addon) if s.get("id") != source_id]
    save_sources(addon, sources)
    _notify(xbmcgui, f"Deleted {source.get('name')}")


def _test_source(addon, xbmcgui, ia_client, params) -> None:
    source_id = params.get("source_id", "")
    source = next((s for s in load_sources(addon) if s.get("id") == source_id), None)
    if source is None:
        return
    try:
        if source.get("kind") == "item":
            item = ia_client.get_item(source.get("value", ""))
            count = len(item.files)
            _notify(xbmcgui, f"{count} files")
        else:
            query = _source_query(source)
            count = sum(1 for _ in ia_client.search(query))
            _notify(xbmcgui, f"{count} results")
    except Exception as exc:
        _notify(xbmcgui, f"Test failed: {exc}")


def _refresh_source(addon, xbmcgui, ia_client, params) -> None:
    source_id = params.get("source_id", "")
    source = next((s for s in load_sources(addon) if s.get("id") == source_id), None)
    if source is None:
        return
    try:
        data = _resolve_source(addon, ia_client, source, xbmcgui, force=True)
        if source.get("kind") == "item":
            count = len(data.get("items", {}).get(source.get("value", ""), []))
        else:
            count = len(data.get("results", []))
        _notify(xbmcgui, f"Refreshed {source.get('name')}: {count}")
    except Exception as exc:
        _notify(xbmcgui, f"Refresh failed: {exc}")


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

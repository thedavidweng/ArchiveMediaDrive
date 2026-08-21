from __future__ import annotations

import os
import shlex
import subprocess
from pathlib import Path
from typing import Iterable

from .ia import ResolvedSource


class RcloneError(RuntimeError):
    pass


LIBRARY_ITEM_LIMIT = 200


def _quote_space_sep(value: str) -> str:
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def _source_sections(
    resolved: Iterable[ResolvedSource], *, library_remote: str, ia_remote: str
) -> tuple[str, str]:
    grouped: dict[str, list[str]] = {}
    seen: set[str] = set()
    for result in resolved:
        for identifier in result.identifiers:
            virtual_path = f"{result.source.path}/{identifier}"
            if virtual_path in seen:
                raise RcloneError(f"duplicate virtual path: {virtual_path}")
            seen.add(virtual_path)
            grouped.setdefault(result.source.path, []).append(identifier)

    if not grouped:
        raise RcloneError("the resolved catalog is empty")

    if len(seen) > LIBRARY_ITEM_LIMIT:
        raise RcloneError(
            f"the resolved catalog has {len(seen)} items; "
            f"the mounted library supports at most {LIBRARY_ITEM_LIMIT} items; "
            "use a channel mode adapter for larger catalogs"
        )

    sections: list[str] = []
    top_upstreams: list[str] = []
    for index, path in enumerate(sorted(grouped)):
        source_remote = f"{library_remote}-src-{index}"
        identifiers = sorted(set(grouped[path]))
        upstreams = " ".join(_quote_space_sep(f"{item}={ia_remote}:{item}") for item in identifiers)
        sections.append(
            f"[{source_remote}]\n"
            "type = combine\n"
            f"upstreams = {upstreams}\n"
        )
        top_upstreams.append(_quote_space_sep(f"{path}={source_remote}:"))

    body = "".join(sections)
    tail = (
        f"[{library_remote}]\n"
        "type = combine\n"
        f"upstreams = {' '.join(top_upstreams)}\n"
        "description = ArchiveMediaDrive virtual library\n"
    )
    return body, tail


def render_config(
    resolved: Iterable[ResolvedSource],
    *,
    library_remote: str,
    ia_remote: str = "archive-media-drive-ia",
) -> str:
    body, tail = _source_sections(resolved, library_remote=library_remote, ia_remote=ia_remote)
    return (
        f"[{ia_remote}]\n"
        "type = internetarchive\n"
        "description = Internet Archive data plane managed by ArchiveMediaDrive\n\n"
        f"{body}"
        f"{tail}"
    )


def is_loopback(address: str) -> bool:
    host = address.rsplit(":", 1)[0].strip("[]")
    return host in {"127.0.0.1", "localhost", "::1"}


def webdav_command(
    *,
    config_path: Path,
    remote_name: str,
    address: str,
    allow_public: bool,
    rclone_binary: str = "rclone",
    extra_args: Iterable[str] = (),
) -> list[str]:
    user = os.environ.get("AMD_WEBDAV_USER")
    password = os.environ.get("AMD_WEBDAV_PASS")
    if bool(user) != bool(password):
        raise RcloneError("AMD_WEBDAV_USER and AMD_WEBDAV_PASS must be set together")
    if not is_loopback(address) and not (user and password) and not allow_public:
        raise RcloneError(
            "refusing unauthenticated WebDAV on a non-loopback address; set credentials or pass --allow-public"
        )
    command = [
        rclone_binary,
        "serve",
        "webdav",
        f"{remote_name}:",
        "--config",
        str(config_path),
        "--read-only",
        "--addr",
        address,
    ]
    if user and password:
        command.extend(["--user", user, "--pass", password])
    command.extend(extra_args)
    return command


def mount_command(
    *,
    config_path: Path,
    remote_name: str,
    mountpoint: Path,
    rclone_binary: str = "rclone",
    extra_args: Iterable[str] = (),
) -> list[str]:
    return [
        rclone_binary,
        "mount",
        f"{remote_name}:",
        str(mountpoint),
        "--config",
        str(config_path),
        "--read-only",
        *extra_args,
    ]


def exec_command(command: list[str]) -> int:
    try:
        return subprocess.run(command, check=False).returncode
    except OSError as exc:
        raise RcloneError(f"failed to execute {shlex.join(command)}: {exc}") from exc

from __future__ import annotations

import os
import shlex
import subprocess
from pathlib import Path
from typing import Iterable

from .ia import ResolvedSource


class RcloneError(RuntimeError):
    pass


def _quote_space_sep(value: str) -> str:
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def combine_upstreams(resolved: Iterable[ResolvedSource], ia_remote: str) -> tuple[str, ...]:
    mappings: list[str] = []
    seen: set[str] = set()
    for result in resolved:
        for identifier in result.identifiers:
            virtual_path = f"{result.source.path}/{identifier}"
            if virtual_path in seen:
                raise RcloneError(f"duplicate virtual path: {virtual_path}")
            seen.add(virtual_path)
            mappings.append(f"{virtual_path}={ia_remote}:{identifier}")
    return tuple(sorted(mappings))


def render_config(
    resolved: Iterable[ResolvedSource],
    *,
    library_remote: str,
    ia_remote: str = "archive-media-drive-ia",
) -> str:
    upstreams = combine_upstreams(resolved, ia_remote)
    if not upstreams:
        raise RcloneError("the resolved catalog is empty")
    encoded = " ".join(_quote_space_sep(item) for item in upstreams)
    return (
        f"[{ia_remote}]\n"
        "type = internetarchive\n"
        "description = Internet Archive data plane managed by ArchiveMediaDrive\n\n"
        f"[{library_remote}]\n"
        "type = combine\n"
        f"upstreams = {encoded}\n"
        "description = ArchiveMediaDrive virtual library\n"
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

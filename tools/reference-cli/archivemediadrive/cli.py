from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from .config import ConfigError, load_config
from .doctor import run_checks
from .model import Paths
from .rclone import RcloneError, exec_command, mount_command, webdav_command
from .sync import synchronize


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="amd",
        description="Expose Internet Archive sources as an rclone-backed virtual drive.",
    )
    parser.add_argument("--config", type=Path, default=Path("config.toml"))
    parser.add_argument("--state-dir", type=Path, default=Path(".amd"))
    parser.add_argument("--ia-binary", default="ia")
    parser.add_argument("--rclone-binary", default="rclone")

    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("doctor", help="check required external tools")
    commands.add_parser("sync", help="resolve configured sources and render rclone config")
    commands.add_parser("catalog", help="print the last synchronized catalog")

    webdav = commands.add_parser("webdav", help="serve the virtual drive over WebDAV")
    webdav.add_argument("--address")
    webdav.add_argument("--allow-public", action="store_true")
    webdav.add_argument("--rclone-arg", action="append", default=[])

    mount = commands.add_parser("mount", help="mount the virtual drive as a local filesystem")
    mount.add_argument("mountpoint", type=Path)
    mount.add_argument("--rclone-arg", action="append", default=[])

    run = commands.add_parser("run", help="synchronize, then serve WebDAV")
    run.add_argument("--address")
    run.add_argument("--allow-public", action="store_true")
    run.add_argument("--rclone-arg", action="append", default=[])
    return parser


def _require_runtime(paths: Paths) -> None:
    if not paths.rclone_config.exists():
        raise RcloneError("rclone config is missing; run `amd sync` first")


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    paths = Paths(args.state_dir)
    try:
        if args.command == "doctor":
            checks = run_checks()
            for check in checks:
                print(f"{'OK' if check.ok else 'FAIL'} {check.name}: {check.detail}")
            return 0 if all(check.ok for check in checks) else 1

        config = load_config(args.config)
        if args.command in {"sync", "run"}:
            resolved = synchronize(config, paths, ia_binary=args.ia_binary)
            count = sum(len(item.identifiers) for item in resolved)
            print(f"synchronized {len(resolved)} sources and {count} virtual item directories")
            if args.command == "sync":
                return 0

        if args.command == "catalog":
            print(json.dumps(json.loads(paths.catalog.read_text(encoding="utf-8")), indent=2))
            return 0

        _require_runtime(paths)
        if args.command in {"webdav", "run"}:
            address = args.address or config.serve.address
            command = webdav_command(
                config_path=paths.rclone_config,
                remote_name=config.serve.remote_name,
                address=address,
                allow_public=args.allow_public,
                rclone_binary=args.rclone_binary,
                extra_args=args.rclone_arg,
            )
            return exec_command(command)

        if args.command == "mount":
            args.mountpoint.mkdir(parents=True, exist_ok=True)
            command = mount_command(
                config_path=paths.rclone_config,
                remote_name=config.serve.remote_name,
                mountpoint=args.mountpoint,
                rclone_binary=args.rclone_binary,
                extra_args=args.rclone_arg,
            )
            return exec_command(command)

        return 2
    except (ConfigError, RcloneError, RuntimeError, OSError, json.JSONDecodeError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

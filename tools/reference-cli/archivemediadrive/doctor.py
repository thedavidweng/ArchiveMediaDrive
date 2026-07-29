from __future__ import annotations

import re
import shutil
import subprocess
from dataclasses import dataclass


@dataclass(frozen=True)
class Check:
    name: str
    ok: bool
    detail: str


def _version(binary: str, args: list[str]) -> Check:
    path = shutil.which(binary)
    if not path:
        return Check(binary, False, "not found in PATH")
    try:
        result = subprocess.run([path, *args], check=False, capture_output=True, text=True)
    except OSError as exc:
        return Check(binary, False, str(exc))
    text = (result.stdout or result.stderr).strip().splitlines()
    detail = text[0] if text else f"exit {result.returncode}"
    return Check(binary, result.returncode == 0, detail)


def run_checks() -> tuple[Check, ...]:
    checks = (
        _version("ia", ["--version"]),
        _version("rclone", ["version"]),
    )
    return checks

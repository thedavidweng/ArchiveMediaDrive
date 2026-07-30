#!/usr/bin/env python3
"""Deterministically vendor pure-Python dependencies for the Kodi release ZIP.

Pins hashes, downloads source archives, verifies SHA-256, installs from the
verified local archives into a temporary directory, removes metadata and
bytecode, rejects compiled extensions, and copies source files into
resources/lib/vendor.
"""
from __future__ import annotations

import hashlib
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

_PLUGIN_ROOT = Path(__file__).resolve().parent / "plugin.video.archivemediadrive"
_VENDOR_DIR = _PLUGIN_ROOT / "resources" / "lib" / "vendor"

_REQUIREMENTS = [
    ("internetarchive", "5.10.1",
     "https://files.pythonhosted.org/packages/b0/95/5d830762f4519bb932401953d4735697a196a48159d2be30c88343023244/internetarchive-5.10.1.tar.gz",
     "7098b34d80ab7cd305d36999185415463207aa15b22ad8686bac06ed39037cae"),
    ("requests", "2.34.2",
     "https://files.pythonhosted.org/packages/ac/c3/e2a2b89f2d3e2179abd6d00ebd70bff6273f37fb3e0cc209f48b39d00cbf/requests-2.34.2.tar.gz",
     "f288924cae4e29463698d6d60bc6a4da69c89185ad1e0bcc4104f584e960b9ed"),
    ("charset-normalizer", "3.4.9",
     "https://files.pythonhosted.org/packages/bd/2a/23f34ec9d04624958e137efdc394888716353190e75f25dd22c7a2c7a8aa/charset_normalizer-3.4.9.tar.gz",
     "673611bbd43f0810bec0b0f028ddeaaa501190339cac411f347ac76917c3ae7b"),
    ("idna", "3.18",
     "https://files.pythonhosted.org/packages/cd/63/9496c57188a2ee585e0f1db071d75089a11e98aa86eb99d9d7618fc1edce/idna-3.18.tar.gz",
     "ffb385a7e039654cef1ab9ef32c6fafe283c0c0467bba1d9029738ce4a14a848"),
    ("urllib3", "2.7.0",
     "https://files.pythonhosted.org/packages/53/0c/06f8b233b8fd13b9e5ee11424ef85419ba0d8ba0b3138bf360be2ff56953/urllib3-2.7.0.tar.gz",
     "231e0ec3b63ceb14667c67be60f2f2c40a518cb38b03af60abc813da26505f4c"),
    ("certifi", "2026.7.22",
     "https://files.pythonhosted.org/packages/a3/c2/24167ea9858356b47a87a50d39908bfdb72ceeefe0041586e704e5376b3a/certifi-2026.7.22.tar.gz",
     "741e2c3b351ddf169a738da9f2c048608ff7f2c5cc02f1ebc6b118bb090d5d55"),
    ("jsonpatch", "1.33",
     "https://files.pythonhosted.org/packages/42/78/18813351fe5d63acad16aec57f94ec2b70a09e53ca98145589e185423873/jsonpatch-1.33.tar.gz",
     "9fcd4009c41e6d12348b4a0ff2563ba56a2923a7dfee731d004e212e1ee5030c"),
    ("jsonpointer", "3.1.1",
     "https://files.pythonhosted.org/packages/18/c7/af399a2e7a67fd18d63c40c5e62d3af4e67b836a2107468b6a5ea24c4304/jsonpointer-3.1.1.tar.gz",
     "0b801c7db33a904024f6004d526dcc53bbb8a4a0f4e32bfd10beadf60adf1900"),
    ("tqdm", "4.70.0",
     "https://files.pythonhosted.org/packages/21/3b/6c24bec5be5e743ffd99576daa5cc077722fc7d5bbc00bd133fa0c698dc6/tqdm-4.70.0.tar.gz",
     "55b0b0dbd97462d06ebee91e4dac24ed4d4702be82b24f07e6c1d27e08cea220"),
]

_COMPILED_EXTENSIONS = {".so", ".pyd", ".dll", ".dylib", ".pyc", ".pyo", ".sh", ".js", ".1"}
_STRIP_DIRS = {"__pycache__", ".dist-info", ".egg-info", "tests", "test", "bin"}


def _download(url: str, dest: Path) -> None:
    subprocess.run(["curl", "-fsSL", "-o", str(dest), url], check=True)


def _verify_sha256(path: Path, expected: str) -> None:
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    if actual != expected:
        raise SystemExit(f"checksum mismatch for {path.name}: expected {expected}, got {actual}")


def _is_pure_python(path: Path) -> bool:
    return path.suffix not in _COMPILED_EXTENSIONS


def _should_strip(name: str) -> bool:
    parts = Path(name).parts
    if "licenses" in parts:
        return False
    return any(part in _STRIP_DIRS or part.endswith(".dist-info") for part in parts)


def _collect_license_texts() -> list[tuple[str, str]]:
    result: list[tuple[str, str]] = []
    if not _VENDOR_DIR.exists():
        return result

    for dist_info in _VENDOR_DIR.rglob("*.dist-info"):
        package = dist_info.name.split("-")[0]
        licenses_dir = dist_info / "licenses"
        if not licenses_dir.is_dir():
            continue

        for license_file in licenses_dir.iterdir():
            if license_file.is_file():
                result.append((f"{package} {license_file.name}", license_file.read_text(encoding="utf-8")))
                break

    return result


def _write_license_txt() -> None:
    root_license = Path(__file__).resolve().parent.parent.parent / "LICENSE"
    parts = [root_license.read_text(encoding="utf-8")]

    vendor_licenses = _collect_license_texts()
    if vendor_licenses:
        parts.append("\n\n================================================================================\n")
        parts.append("THIRD-PARTY LICENSES\n")
        parts.append("================================================================================\n")
        for package, text in vendor_licenses:
            parts.append(f"\n--- {package} ---\n\n")
            parts.append(text)

    (_PLUGIN_ROOT / "LICENSE.txt").write_text("".join(parts), encoding="utf-8")


def build() -> None:
    if _VENDOR_DIR.exists():
        shutil.rmtree(_VENDOR_DIR)
    _VENDOR_DIR.mkdir(parents=True)

    with tempfile.TemporaryDirectory(prefix="amd-vendor-") as tmpdir:
        tmp = Path(tmpdir)
        archives: list[Path] = []

        for name, version, url, expected_hash in _REQUIREMENTS:
            archive = tmp / f"{name}-{version}.tar.gz"
            print(f"downloading {name}=={version}...")
            _download(url, archive)
            _verify_sha256(archive, expected_hash)
            archives.append(archive)

        install_dir = tmp / "install"
        install_dir.mkdir(parents=True, exist_ok=True)

        for archive in archives:
            print(f"installing {archive.name} from verified local archive...")
            subprocess.run(
                [sys.executable, "-m", "pip", "install",
                 "--no-deps",
                 "--target", str(install_dir),
                 str(archive)],
                check=True,
            )

        for src in install_dir.rglob("*"):
            if src.is_file() and _is_pure_python(src) and not _should_strip(str(src.relative_to(install_dir))):
                rel = src.relative_to(install_dir)
                dest = _VENDOR_DIR / rel
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(src, dest)
                dest.chmod(0o644)

    init_file = _VENDOR_DIR / "__init__.py"
    if not init_file.exists():
        init_file.write_text("", encoding="utf-8")

    _write_license_txt()

    print(f"vendored {len(list(_VENDOR_DIR.rglob('*.py')))} Python files into {_VENDOR_DIR}")


if __name__ == "__main__":
    build()

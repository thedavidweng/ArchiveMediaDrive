from __future__ import annotations

import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main() -> None:
    tag = sys.argv[1]
    match = re.fullmatch(r"(kodi|jellyfin|emby)-v(\d+)\.(\d+)\.(\d+)", tag)
    if not match:
        print(f"invalid tag {tag}", file=sys.stderr)
        raise SystemExit(1)

    platform = match.group(1)
    version = f"{match.group(2)}.{match.group(3)}.{match.group(4)}"

    with open("Directory.Build.props") as f:
        core_version = re.search(r"<Version>([^<]+)</Version>", f.read()).group(1)

    if platform == "kodi":
        tree = ET.parse("plugins/kodi/plugin.video.archivemediadrive/addon.xml")
        addon_version = tree.getroot().get("version")
        if addon_version != version:
            print(f"kodi addon version {addon_version} does not match tag {version}", file=sys.stderr)
            raise SystemExit(1)
    else:
        if core_version != version:
            print(f"{platform} core version {core_version} does not match tag {version}", file=sys.stderr)
            raise SystemExit(1)

    if platform == "jellyfin":
        manifest = json.load(open("plugins/jellyfin/manifest.json"))
        manifest_version = manifest[0]["versions"][0]["version"]
        if manifest_version != version:
            print(f"jellyfin manifest version {manifest_version} does not match tag {version}", file=sys.stderr)
            raise SystemExit(1)

    artifact_name = {
        "kodi": "ArchiveMediaDrive.Kodi",
        "jellyfin": "ArchiveMediaDrive.Jellyfin",
        "emby": "ArchiveMediaDrive.Emby",
    }[platform]

    print(f"platform={platform}")
    print(f"version={version}")
    print(f"artifact_name={artifact_name}")

    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        with open(output, "a") as f:
            f.write(f"platform={platform}\n")
            f.write(f"version={version}\n")
            f.write(f"artifact_name={artifact_name}\n")


if __name__ == "__main__":
    main()

from __future__ import annotations

import datetime
import hashlib
import json
import re
import sys
from pathlib import Path


def main() -> None:
    tag = sys.argv[1]
    zip_path = Path(sys.argv[2])
    manifest_path = Path(sys.argv[3])
    out_path = Path(sys.argv[4])

    sha256 = hashlib.sha256(zip_path.read_bytes()).hexdigest()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    entry = manifest[0]["versions"][0]
    entry["checksum"] = f"sha256:{sha256}"
    entry["timestamp"] = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    entry["sourceUrl"] = (
        f"https://github.com/thedavidweng/ArchiveMediaDrive/releases/download/"
        f"{tag}/{re.search(r'[^/]+$', zip_path.name).group(0)}"
    )

    out_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()

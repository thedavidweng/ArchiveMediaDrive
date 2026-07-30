from __future__ import annotations

import hashlib
from pathlib import Path


def main() -> None:
    dist = Path("dist")
    sums = []
    for path in sorted(dist.iterdir()):
        if path.is_file() and path.name != "sha256sums.txt":
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            sums.append(f"{digest}  {path.name}")
    (dist / "sha256sums.txt").write_text("\n".join(sums) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()

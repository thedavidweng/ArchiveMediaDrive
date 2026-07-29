import sys
from pathlib import Path

_PLUGIN_ROOT = Path(__file__).resolve().parents[2] / "plugin.video.archivemediadrive"
if str(_PLUGIN_ROOT) not in sys.path:
    sys.path.insert(0, str(_PLUGIN_ROOT))

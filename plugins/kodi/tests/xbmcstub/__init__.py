import sys
from pathlib import Path

_PLUGIN_ROOT = Path(__file__).resolve().parents[2] / "plugin.video.archivemediadrive"
if str(_PLUGIN_ROOT) not in sys.path:
    sys.path.insert(0, str(_PLUGIN_ROOT))

_VENDORS_ROOT = Path(__file__).resolve().parents[2] / "vendor"
if _VENDORS_ROOT.is_dir() and str(_VENDORS_ROOT) not in sys.path:
    sys.path.insert(0, str(_VENDORS_ROOT))

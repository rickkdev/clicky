#!/usr/bin/env python3
"""minimal explain demo for local development.

requires CLICKY_BRIDGE_COMMAND once the native bridge exists.
"""

import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "hermes_clicky_plugin",
    ROOT / "__init__.py",
    submodule_search_locations=[str(ROOT)],
)
plugin = importlib.util.module_from_spec(spec)
sys.modules["hermes_clicky_plugin"] = plugin
assert spec.loader is not None
spec.loader.exec_module(plugin)

from hermes_clicky_plugin.tools import explain_clicky_screen  # noqa: E402

print(explain_clicky_screen({
    "task": "explain what controls are visible and what the user should click next"
}))

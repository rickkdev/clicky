#!/usr/bin/env python3
"""validate the self-contained hermes-clicky-plugin package."""

from __future__ import annotations

import ast
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def fail(message: str) -> None:
    print(f"fail: {message}", file=sys.stderr)
    raise SystemExit(1)


def load_json(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        fail(f"invalid json {path.relative_to(ROOT)}: {exc}")


def validate_plugin_yaml() -> None:
    path = ROOT / "plugin.yaml"
    text = path.read_text(encoding="utf-8")
    for required in [
        "name: hermes-clicky-plugin",
        "provides_tools:",
        "get_clicky_capabilities",
        "observe_clicky_screen",
        "explain_clicky_screen",
        "point_clicky_target",
    ]:
        if required not in text:
            fail(f"plugin.yaml missing {required!r}")


def validate_python_syntax() -> None:
    for path in [ROOT / "__init__.py", ROOT / "schemas.py", ROOT / "tools.py", ROOT / "bridge_client.py"]:
        try:
            ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        except SyntaxError as exc:
            fail(f"python syntax error in {path.relative_to(ROOT)}: {exc}")


def validate_protocol_json() -> None:
    schema = load_json(ROOT / "protocol" / "schema.json")
    if "$defs" not in schema:
        fail("protocol/schema.json missing $defs")

    examples = sorted((ROOT / "protocol" / "examples").glob("*.json"))
    if not examples:
        fail("no protocol examples found")

    for example in examples:
        data = load_json(example)
        if "ok" not in data or "status" not in data:
            fail(f"example {example.name} missing ok/status")


def main() -> None:
    validate_plugin_yaml()
    validate_python_syntax()
    validate_protocol_json()
    print("ok: hermes-clicky-plugin validates")


if __name__ == "__main__":
    main()

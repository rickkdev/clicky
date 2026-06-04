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
    for path in [
        ROOT / "__init__.py",
        ROOT / "schemas.py",
        ROOT / "tools.py",
        ROOT / "bridge_client.py",
        ROOT / "permission_policy.py",
        ROOT / "safety_policy.py",
        ROOT / "confirmation_state.py",
    ]:
        try:
            ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
        except SyntaxError as exc:
            fail(f"python syntax error in {path.relative_to(ROOT)}: {exc}")


def resolve_ref(schema: dict, ref: str) -> dict:
    prefix = "#/$defs/"
    if not ref.startswith(prefix):
        fail(f"unsupported schema ref {ref}")
    name = ref[len(prefix):]
    try:
        return schema["$defs"][name]
    except KeyError:
        fail(f"schema ref points to missing definition {name}")


def validate_against_schema(schema: dict, node: dict, value, path: str) -> None:
    if "$ref" in node:
        validate_against_schema(schema, resolve_ref(schema, node["$ref"]), value, path)
        return
    if "allOf" in node:
        for idx, item in enumerate(node["allOf"]):
            validate_against_schema(schema, item, value, f"{path}.allOf[{idx}]")
        return
    if "oneOf" in node:
        matches = 0
        errors = []
        for item in node["oneOf"]:
            try:
                validate_against_schema(schema, item, value, path)
                matches += 1
            except ValueError as exc:
                errors.append(str(exc))
        if matches != 1:
            raise ValueError(f"{path}: expected exactly one schema match, got {matches}; errors={errors[:3]}")
        return

    if "const" in node and value != node["const"]:
        raise ValueError(f"{path}: expected const {node['const']!r}, got {value!r}")
    if "enum" in node and value not in node["enum"]:
        raise ValueError(f"{path}: expected one of {node['enum']!r}, got {value!r}")

    node_type = node.get("type")
    if node_type == "object":
        if not isinstance(value, dict):
            raise ValueError(f"{path}: expected object")
        for required in node.get("required", []):
            if required not in value:
                raise ValueError(f"{path}: missing required field {required}")
        props = node.get("properties", {})
        if node.get("additionalProperties") is False:
            extras = sorted(set(value) - set(props))
            if extras:
                raise ValueError(f"{path}: unexpected fields {extras}")
        for key, child in props.items():
            if key in value:
                validate_against_schema(schema, child, value[key], f"{path}.{key}")
    elif node_type == "array":
        if not isinstance(value, list):
            raise ValueError(f"{path}: expected array")
        if len(value) < node.get("minItems", 0):
            raise ValueError(f"{path}: expected at least {node['minItems']} items")
        child = node.get("items")
        if child:
            for idx, item in enumerate(value):
                validate_against_schema(schema, child, item, f"{path}[{idx}]")
    elif node_type == "string":
        if not isinstance(value, str):
            raise ValueError(f"{path}: expected string")
        if len(value) < node.get("minLength", 0):
            raise ValueError(f"{path}: string shorter than minLength")
    elif node_type == "boolean":
        if not isinstance(value, bool):
            raise ValueError(f"{path}: expected boolean")
    elif node_type == "integer":
        if not isinstance(value, int) or isinstance(value, bool):
            raise ValueError(f"{path}: expected integer")
    elif node_type == "number":
        if not isinstance(value, (int, float)) or isinstance(value, bool):
            raise ValueError(f"{path}: expected number")
        if "minimum" in node and value < node["minimum"]:
            raise ValueError(f"{path}: below minimum {node['minimum']}")
        if "maximum" in node and value > node["maximum"]:
            raise ValueError(f"{path}: above maximum {node['maximum']}")


def validate_protocol_json() -> None:
    schema = load_json(ROOT / "protocol" / "schema.json")
    defs = schema.get("$defs")
    if not isinstance(defs, dict):
        fail("protocol/schema.json missing $defs")

    for name in [
        "getCapabilitiesRequest",
        "getCapabilitiesResponse",
        "observeScreenRequest",
        "observeScreenResponse",
        "explainScreenRequest",
        "explainScreenResponse",
        "pointToTargetRequest",
        "pointToTargetResponse",
        "actionProposal",
        "actionProposalsResponse",
        "permissionTier",
        "permissionDecision",
        "riskFlag",
        "safetyDecision",
        "confirmationRequest",
        "confirmationResponse",
    ]:
        if name not in defs:
            fail(f"protocol/schema.json missing {name}")

    doc = ROOT / "protocol" / "README.md"
    if not doc.exists():
        fail("protocol/README.md missing")
    doc_text = doc.read_text(encoding="utf-8")
    for method in ["getCapabilities", "observeScreen", "explainScreen", "pointToTarget", "action proposals", "confirmation"]:
        if method not in doc_text:
            fail(f"protocol/README.md missing {method}")

    examples = sorted((ROOT / "protocol" / "examples").glob("*.json"))
    if not examples:
        fail("no protocol examples found")

    for required in [
        "capabilities.windows.json",
        "observe.metadata-only.json",
        "explain.success.json",
        "point.success.json",
        "point.low-confidence.json",
        "action.low-risk-click.json",
        "action.high-risk-destructive.json",
        "action.blocked.json",
        "confirmation.required.json",
        "confirmation.cancelled.json",
        "confirmation.approved-rechecked.json",
        "confirmation.stale.json",
    ]:
        if not (ROOT / "protocol" / "examples" / required).exists():
            fail(f"missing required example {required}")

    for example in examples:
        data = load_json(example)
        if data.get("protocolVersion") != "clicky.hermes.v1":
            fail(f"example {example.name} missing protocolVersion")
        if "ok" not in data or "status" not in data:
            fail(f"example {example.name} missing ok/status")
        try:
            validate_against_schema(schema, schema, data, example.name)
        except ValueError as exc:
            fail(f"example {example.name} does not match protocol schema: {exc}")


def main() -> None:
    validate_plugin_yaml()
    validate_python_syntax()
    validate_protocol_json()
    print("ok: hermes-clicky-plugin validates")


if __name__ == "__main__":
    main()

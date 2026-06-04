#!/usr/bin/env python3
"""Hermes Clicky V1 demo workflow.

Flow:
1. user asks where to click
2. Hermes calls pointToTarget
3. Clicky renders/attempts pointer overlay
4. Hermes responds with the target explanation

Default is deterministic and safe: if no bridge command is passed, this uses the
plugin fake bridge instead of launching a native tray app.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from pathlib import Path
from time import perf_counter
from typing import Any, Callable

ROOT = Path(__file__).resolve().parents[1]
FAKE_BRIDGE = ROOT / "scripts" / "fake_bridge.py"

spec = importlib.util.spec_from_file_location(
    "hermes_clicky_plugin",
    ROOT / "__init__.py",
    submodule_search_locations=[str(ROOT)],
)
plugin = importlib.util.module_from_spec(spec)
sys.modules["hermes_clicky_plugin"] = plugin
assert spec.loader is not None
spec.loader.exec_module(plugin)

from hermes_clicky_plugin.bridge_client import BridgeConfig, ClickyBridgeClient, ClickyBridgeError  # noqa: E402

PROTOCOL_VERSION = "clicky.hermes.v1"

FAILURE_CASES = [
    {
        "case": "Failure case: bridge unavailable",
        "userBehavior": "Hermes says Clicky is not configured and suggests setting CLICKY_BRIDGE_COMMAND.",
        "status": "bridge_unavailable",
    },
    {
        "case": "Failure case: screen capture permission denied",
        "userBehavior": "Hermes asks the user to grant Screen Recording / screen_capture permission before retrying.",
        "status": "permission_denied",
    },
    {
        "case": "Failure case: low confidence target",
        "userBehavior": "Hermes does not pretend to know where to click; it asks for clarification or suggests observe/explain first.",
        "status": "low_confidence",
    },
    {
        "case": "Failure case: overlay unavailable",
        "userBehavior": "Hermes still returns coordinates and explanation, but states that no visual pointer was rendered.",
        "status": "overlay_unavailable",
    },
]


def elapsed_ms(start: float) -> float:
    return round((perf_counter() - start) * 1000, 3)


def timed_call(client: ClickyBridgeClient, method: str, params: dict[str, Any]) -> tuple[dict[str, Any], float]:
    started = perf_counter()
    result = client.call(method, params)
    return result, elapsed_ms(started)


def build_hermes_response(target: str, point: dict[str, Any], explanation: str) -> str:
    if point.get("status") == "pointed":
        overlay_copy = "I rendered the pointer" if point.get("overlayRendered") else "I found the spot, but did not render an overlay"
        label = point.get("label") or target
        return f"{overlay_copy} on {label}. {explanation}"
    return f"I could not confidently point to {target}. {explanation}"


def run_demo(args: argparse.Namespace) -> dict[str, Any]:
    command = args.bridge_command or f"{sys.executable} {FAKE_BRIDGE}"
    client = ClickyBridgeClient(BridgeConfig(command=command, timeout_seconds=args.timeout))
    total_started = perf_counter()
    steps: list[dict[str, Any]] = []

    health, bridge_startup_ms = timed_call(client, "rpc.health", {})
    steps.append({"name": "bridgeHealth", "status": health.get("status"), "bridge": health.get("bridge")})

    observe, screen_capture_ms = timed_call(client, "clicky.observeScreen", {"imageMode": "metadataOnly"})
    steps.append({
        "name": "observeScreen",
        "status": observe.get("status"),
        "observationId": observe.get("observationId"),
        "displays": len(observe.get("displays") or []),
    })

    point_params = {
        "target": args.target,
        "task": args.task,
        "screenId": args.screen_id,
        "renderOverlay": args.render_overlay,
    }
    point, model_response_ms = timed_call(client, "clicky.pointToTarget", point_params)
    steps.append({
        "name": "pointToTarget",
        "status": point.get("status"),
        "overlayRendered": bool(point.get("overlayRendered")),
        "label": point.get("label"),
    })

    explanation_task = f"explain the highlighted target '{point.get('label') or args.target}' for the user task: {args.task}"
    explain, explain_ms = timed_call(client, "clicky.explainScreen", {
        "task": explanation_task,
        "observationId": observe.get("observationId"),
        "screenId": args.screen_id,
    })
    steps.append({"name": "explainScreen", "status": explain.get("status")})

    explanation = str(explain.get("explanation") or "").strip()
    hermes_response = build_hermes_response(args.target, point, explanation)
    steps.append({"name": "hermesResponse", "text": hermes_response})

    # The stdio bridge returns overlayRendered but does not expose a separate overlay duration yet.
    # Keep the timing key explicit so demo logs remain stable when native hosts add it.
    overlay_render_ms = 0.0 if not point.get("overlayRendered") else model_response_ms

    capabilities_platform = args.platform or "windows"
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "demo_completed",
        "platform": capabilities_platform,
        "userRequest": args.task,
        "target": args.target,
        "steps": steps,
        "observation": {
            "status": observe.get("status"),
            "observationId": observe.get("observationId"),
            "displayCount": len(observe.get("displays") or []),
        },
        "pointing": {
            "status": point.get("status"),
            "label": point.get("label"),
            "confidence": point.get("confidence"),
            "normalized": point.get("normalized"),
            "physical": point.get("physical"),
            "overlayRendered": bool(point.get("overlayRendered")),
        },
        "explanation": explanation,
        "hermesResponse": hermes_response,
        "timingMs": {
            "bridgeStartupMs": bridge_startup_ms,
            "screenCaptureMs": screen_capture_ms,
            "modelResponseMs": round(model_response_ms + explain_ms, 3),
            "overlayRenderMs": overlay_render_ms,
            "totalMs": elapsed_ms(total_started),
        },
        "failureCases": FAILURE_CASES,
    }


def print_human(result: dict[str, Any]) -> None:
    print("Hermes Clicky demo completed")
    print(f"user: {result['userRequest']}")
    print(f"target: {result['target']}")
    print(f"response: {result['hermesResponse']}")
    print("timingMs:")
    for key, value in result["timingMs"].items():
        print(f"  {key}: {value}")
    print("steps:")
    for step in result["steps"]:
        print(f"  - {step['name']}: {step.get('status', 'ok')}")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run the Hermes Clicky point/explain demo workflow.")
    parser.add_argument("--bridge-command", help="JSON-RPC stdio bridge command. Defaults to deterministic fake bridge.")
    parser.add_argument("--platform", default="windows", choices=["windows", "macos"], help="Demo platform label.")
    parser.add_argument("--target", default="settings", help="Visible target to point at.")
    parser.add_argument("--task", default="show me where to click next", help="User request text.")
    parser.add_argument("--screen-id", default=None, help="Optional screen/display id.")
    parser.add_argument("--timeout", type=int, default=5, help="Bridge timeout seconds.")
    parser.add_argument("--no-render-overlay", dest="render_overlay", action="store_false", help="Dry-run without overlay rendering.")
    parser.add_argument("--json", action="store_true", help="Print machine-readable JSON.")
    parser.set_defaults(render_overlay=True)
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    try:
        result = run_demo(args)
    except ClickyBridgeError as exc:
        result = {
            "protocolVersion": PROTOCOL_VERSION,
            "ok": False,
            "status": exc.code,
            "error": exc.to_dict(),
            "failureCases": FAILURE_CASES,
        }
        if args.json:
            print(json.dumps(result, ensure_ascii=False, indent=2))
        else:
            print(f"demo failed: {exc.code}: {exc.message}", file=sys.stderr)
        return 1

    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print_human(result)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

#!/usr/bin/env python3
"""deterministic json-rpc stdio bridge for us-003 contract tests.

this is not a native clicky runtime. it proves the plugin-to-bridge transport
shape before windows/mac backends exist.
"""

from __future__ import annotations

import json
import sys
from datetime import datetime, timezone
from typing import Any

PROTOCOL_VERSION = "clicky.hermes.v1"
PERMISSION_TIERS = ["observe", "point", "confirmBeforeAction", "scopedAutopilot", "fullControl"]


def protocol_error(code: str, message: str, *, retryable: bool = False, permission: str | None = None) -> dict[str, Any]:
    return {
        "code": code,
        "message": message,
        "retryable": retryable,
        "permission": permission,
    }


def health(params: dict[str, Any]) -> dict[str, Any]:
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "ok",
        "transport": "stdio",
        "bridge": "fake",
    }


def get_capabilities(params: dict[str, Any]) -> dict[str, Any]:
    platform = params.get("platform") or "windows"
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "ready",
        "platform": platform,
        "bridge": {"transport": "stdio", "runtime": "fake"},
        "capabilities": {
            "observeScreen": {"enabled": True},
            "explainScreen": {"enabled": True},
            "pointToTarget": {"enabled": True},
            "overlay": {"enabled": True},
            "osControl": {"enabled": False, "reason": "phase_2_not_implemented"},
        },
        "activePermissionTier": "confirmBeforeAction",
        "defaultPermissionTier": "confirmBeforeAction",
        "availablePermissionTiers": PERMISSION_TIERS,
        "permissionTierChange": "explicit_user_setting_or_command_required",
        "fullControlPolicy": "external_user_configuration_only",
    }


def observe_screen(params: dict[str, Any]) -> dict[str, Any]:
    image_mode = params.get("imageMode") or "metadataOnly"
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "observed",
        "observationId": "fake-observation-001",
        "platform": "windows",
        "capturedAt": datetime(2026, 1, 1, tzinfo=timezone.utc).isoformat().replace("+00:00", "Z"),
        "coordinateSpace": {"kind": "normalized", "origin": "top_left", "xRange": [0, 1], "yRange": [0, 1]},
        "displays": [
            {
                "id": "fake-display-1",
                "primary": True,
                "bounds": {"x": 0, "y": 0, "width": 1920, "height": 1080},
                "scale": 1.0,
            }
        ],
        "images": [] if image_mode == "metadataOnly" else [{"mode": image_mode, "available": False, "reason": "fake_bridge_no_image_bytes"}],
    }


def explain_screen(params: dict[str, Any]) -> dict[str, Any]:
    task = str(params.get("task") or "").strip()
    if not task:
        raise BridgeProtocolError("invalid_request", "task is required", retryable=False)
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "explained",
        "explanation": f"fake bridge explanation for: {task}",
        "regions": [],
    }


def point_to_target(params: dict[str, Any]) -> dict[str, Any]:
    target = str(params.get("target") or "").strip()
    if not target:
        raise BridgeProtocolError("invalid_request", "target is required", retryable=False)
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "pointed",
        "target": target,
        "label": target,
        "confidence": 0.99,
        "normalized": {"x": 0.5, "y": 0.5},
        "physical": {"x": 960, "y": 540, "displayId": "fake-display-1"},
        "reasoning": "deterministic fake bridge target at screen center",
        "overlayRendered": bool(params.get("renderOverlay", True)),
    }


class BridgeProtocolError(Exception):
    def __init__(self, code: str, message: str, *, retryable: bool = False, permission: str | None = None):
        super().__init__(message)
        self.error = protocol_error(code, message, retryable=retryable, permission=permission)


ROUTES = {
    "rpc.health": health,
    "clicky.getCapabilities": get_capabilities,
    "clicky.observeScreen": observe_screen,
    "clicky.explainScreen": explain_screen,
    "clicky.pointToTarget": point_to_target,
}


def handle_request(request: dict[str, Any]) -> dict[str, Any]:
    request_id = request.get("id")
    method = request.get("method")
    params = request.get("params") or {}
    if request.get("jsonrpc") != "2.0":
        return {"jsonrpc": "2.0", "id": request_id, "error": protocol_error("invalid_request", "jsonrpc must be 2.0")}
    if not isinstance(params, dict):
        return {"jsonrpc": "2.0", "id": request_id, "error": protocol_error("invalid_request", "params must be an object")}
    if method == "clicky.forcePermissionDenied":
        return {
            "jsonrpc": "2.0",
            "id": request_id,
            "error": protocol_error(
                "permission_denied",
                "screen recording permission is required",
                retryable=False,
                permission="screen_recording",
            ),
        }
    route = ROUTES.get(method)
    if route is None:
        return {"jsonrpc": "2.0", "id": request_id, "error": protocol_error("method_not_found", f"unknown method: {method}")}
    try:
        return {"jsonrpc": "2.0", "id": request_id, "result": route(params)}
    except BridgeProtocolError as exc:
        return {"jsonrpc": "2.0", "id": request_id, "error": exc.error}


def main() -> int:
    line = sys.stdin.readline()
    if not line:
        return 0
    try:
        request = json.loads(line)
    except json.JSONDecodeError:
        response = {"jsonrpc": "2.0", "id": None, "error": protocol_error("invalid_json", "request was not valid json")}
    else:
        response = handle_request(request)
    print(json.dumps(response, ensure_ascii=False), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

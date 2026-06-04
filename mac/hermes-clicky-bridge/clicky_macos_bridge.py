#!/usr/bin/env python3
"""macOS JSON-RPC stdio bridge for Hermes Clicky V1.

This bridge is deliberately narrow: capabilities, observe, explain, point.
It does not execute OS control and does not launch the tray app.
"""

from __future__ import annotations

import base64
import ctypes
import json
import os
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

PROTOCOL_VERSION = "clicky.hermes.v1"


def protocol_error(code: str, message: str, *, retryable: bool = False, permission: str | None = None) -> dict[str, Any]:
    error = {"code": code, "message": message, "retryable": retryable}
    if permission:
        error["permission"] = permission
    return error


class BridgeProtocolError(Exception):
    def __init__(self, code: str, message: str, *, retryable: bool = False, permission: str | None = None):
        super().__init__(message)
        self.error = protocol_error(code, message, retryable=retryable, permission=permission)


def env_bool(name: str, default: bool | None = None) -> bool | None:
    value = os.environ.get(name)
    if value is None:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


def permission_state() -> tuple[bool, bool]:
    screen = env_bool("CLICKY_MAC_BRIDGE_SCREEN_RECORDING")
    access = env_bool("CLICKY_MAC_BRIDGE_ACCESSIBILITY")
    return (
        probe_screen_recording() if screen is None else screen,
        probe_accessibility() if access is None else access,
    )


def probe_screen_recording() -> bool:
    try:
        app_services = ctypes.cdll.LoadLibrary("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")
        app_services.CGPreflightScreenCaptureAccess.restype = ctypes.c_bool
        return bool(app_services.CGPreflightScreenCaptureAccess())
    except Exception:
        return False


def probe_accessibility() -> bool:
    try:
        app_services = ctypes.cdll.LoadLibrary("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")
        app_services.AXIsProcessTrusted.restype = ctypes.c_bool
        return bool(app_services.AXIsProcessTrusted())
    except Exception:
        return False


def permission(granted: bool, reason: str) -> dict[str, Any]:
    value: dict[str, Any] = {"granted": granted}
    if not granted:
        value["reason"] = reason
    return value


def capability(enabled: bool, reason: str | None = None) -> dict[str, Any]:
    value: dict[str, Any] = {"enabled": enabled}
    if reason:
        value["reason"] = reason
    return value


def get_capabilities(params: dict[str, Any]) -> dict[str, Any]:
    screen, access = permission_state()
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "ready",
        "platform": "macos",
        "bridge": {"transport": "stdio", "runtime": "macos"},
        "permissions": {
            "screenRecording": permission(screen, "missing_screen_recording_permission"),
            "accessibility": permission(access, "missing_accessibility_permission"),
        },
        "capabilities": {
            "observeScreen": capability(screen, None if screen else "missing_screen_recording_permission"),
            "explainScreen": capability(screen and bool(os.environ.get("CLICKY_MAC_BRIDGE_EXPLANATION_RESPONSE")), None if screen and os.environ.get("CLICKY_MAC_BRIDGE_EXPLANATION_RESPONSE") else ("missing_screen_recording_permission" if not screen else "missing_explanation_response_config")),
            "pointToTarget": capability(screen and bool(os.environ.get("CLICKY_MAC_BRIDGE_POINT_RESPONSE")), None if screen and os.environ.get("CLICKY_MAC_BRIDGE_POINT_RESPONSE") else ("missing_screen_recording_permission" if not screen else "missing_point_response_config")),
            "overlay": capability(access, None if access else "missing_accessibility_permission"),
            "osControl": capability(False, "phase_2_not_implemented"),
        },
    }


def require_screen_capture() -> None:
    screen, _ = permission_state()
    if not screen:
        raise BridgeProtocolError(
            "permission_denied",
            "macOS Screen Recording permission is required for screen capture.",
            retryable=False,
            permission="screen_capture",
        )


def default_display() -> dict[str, Any]:
    displays_json = os.environ.get("CLICKY_MAC_BRIDGE_DISPLAYS_JSON")
    if displays_json:
        displays = json.loads(displays_json)
        if isinstance(displays, list) and displays:
            return displays[0]
    return {
        "id": "macos-display-1",
        "label": "Main Display",
        "primary": True,
        "bounds": {"x": 0, "y": 0, "width": 1440, "height": 900},
        "scale": 2.0,
        "coordinateScale": {"scaleX": 2.0, "scaleY": 2.0},
        "isCursorScreen": True,
    }


def observe_screen(params: dict[str, Any]) -> dict[str, Any]:
    require_screen_capture()
    image_mode = params.get("imageMode") or "metadataOnly"
    display = default_display()
    image = {
        "mode": image_mode,
        "width": int(display["bounds"]["width"] * display.get("scale", 1.0)),
        "height": int(display["bounds"]["height"] * display.get("scale", 1.0)),
        "mime": "image/jpeg",
        "displayId": display["id"],
    }

    if image_mode in {"file", "base64"}:
        capture_path = capture_screen_to_file(params)
        if image_mode == "file":
            image["path"] = str(capture_path)
        else:
            max_bytes = int(params.get("maxBase64Bytes") or 1_000_000)
            data = capture_path.read_bytes()
            if len(data) <= max_bytes:
                image["data"] = base64.b64encode(data).decode("ascii")
            else:
                image["truncated"] = True
                image["reason"] = "maxBase64Bytes_exceeded"
            try:
                capture_path.unlink()
            except OSError:
                pass

    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "observed",
        "observationId": f"macos-observation-{datetime.now(timezone.utc).strftime('%Y%m%d%H%M%S')}",
        "platform": "macos",
        "capturedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "coordinateSpace": "physical_pixels",
        "displays": [display],
        "images": [image],
        "cursor": {"screenId": display["id"], "isOnScreen": True, "position": None},
    }


def capture_screen_to_file(params: dict[str, Any]) -> Path:
    output_dir = Path(params.get("outputDirectory") or tempfile.gettempdir())
    output_dir.mkdir(parents=True, exist_ok=True)
    path = output_dir / f"clicky-macos-{datetime.now(timezone.utc).strftime('%Y%m%d%H%M%S%f')}.jpg"
    fake_b64 = os.environ.get("CLICKY_MAC_BRIDGE_FAKE_IMAGE_BASE64")
    if fake_b64:
        path.write_bytes(base64.b64decode(fake_b64))
        return path
    try:
        subprocess.run(["/usr/sbin/screencapture", "-x", "-t", "jpg", str(path)], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
        return path
    except Exception as exc:
        raise BridgeProtocolError("capture_failed", f"macOS screen capture failed: {exc}", retryable=True)


def explain_screen(params: dict[str, Any]) -> dict[str, Any]:
    require_screen_capture()
    task = str(params.get("task") or "").strip()
    if not task:
        raise BridgeProtocolError("invalid_request", "task is required", retryable=False)
    response = os.environ.get("CLICKY_MAC_BRIDGE_EXPLANATION_RESPONSE")
    if not response:
        raise BridgeProtocolError("bridge_unavailable", "CLICKY_MAC_BRIDGE_EXPLANATION_RESPONSE is required for standalone macOS explanation.", retryable=False)
    parsed: dict[str, Any]
    if response.strip().startswith("{"):
        parsed = json.loads(response)
    else:
        parsed = {"explanation": response, "regions": []}
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "explained",
        "explanation": str(parsed.get("explanation") or "").strip(),
        "regions": parsed.get("regions") or [],
    }


def point_to_target(params: dict[str, Any]) -> dict[str, Any]:
    require_screen_capture()
    target = str(params.get("target") or "").strip()
    if not target:
        raise BridgeProtocolError("invalid_request", "target is required", retryable=False)
    response = os.environ.get("CLICKY_MAC_BRIDGE_POINT_RESPONSE")
    if not response:
        raise BridgeProtocolError("bridge_unavailable", "CLICKY_MAC_BRIDGE_POINT_RESPONSE is required for standalone macOS pointing.", retryable=False)
    parsed = json.loads(response)
    status = parsed.get("status") or "pointed"
    ok = status == "pointed"
    _, access = permission_state()
    render_overlay = bool(params.get("renderOverlay", True))
    overlay_rendered = bool(render_overlay and access and env_bool("CLICKY_MAC_BRIDGE_OVERLAY_RENDERED", False))
    result = {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": ok,
        "status": status,
        "target": target,
        "label": parsed.get("label") or target,
        "confidence": parsed.get("confidence"),
        "normalized": parsed.get("normalized"),
        "physical": parsed.get("physical"),
        "reasoning": parsed.get("reasoning"),
        "overlayRendered": overlay_rendered,
    }
    if not ok:
        result["error"] = protocol_error(status, f"macOS bridge could not point to {target}", retryable=True)
    return result


ROUTES = {
    "rpc.health": lambda params: {"protocolVersion": PROTOCOL_VERSION, "ok": True, "status": "ok", "transport": "stdio", "bridge": "macos"},
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
    route = ROUTES.get(method)
    if route is None:
        return {"jsonrpc": "2.0", "id": request_id, "error": protocol_error("method_not_found", f"unknown method: {method}")}
    try:
        return {"jsonrpc": "2.0", "id": request_id, "result": route(params)}
    except BridgeProtocolError as exc:
        return {"jsonrpc": "2.0", "id": request_id, "error": exc.error}
    except Exception as exc:
        return {"jsonrpc": "2.0", "id": request_id, "error": protocol_error("bridge_error", str(exc), retryable=True)}


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

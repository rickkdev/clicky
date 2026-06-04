"""tool handlers for hermes-clicky-plugin."""

from __future__ import annotations

import json
import platform as platform_module
from typing import Any

from .bridge_client import ClickyBridgeClient, ClickyBridgeError


def _json(data: dict[str, Any]) -> str:
    return json.dumps(data, ensure_ascii=False)


def _platform_name(requested: str | None = None) -> str:
    if requested and requested != "auto":
        return requested
    sys = platform_module.system().lower()
    if sys == "darwin":
        return "macos"
    if sys == "windows":
        return "windows"
    return sys or "unknown"


def _bridge_or_error(method: str, params: dict[str, Any]) -> dict[str, Any]:
    try:
        return ClickyBridgeClient().call(method, params)
    except ClickyBridgeError as exc:
        return {
            "ok": False,
            "status": exc.code,
            "error": exc.to_dict(),
        }
    except Exception as exc:  # plugin handlers must never raise into hermes
        return {
            "ok": False,
            "status": "plugin_error",
            "error": {"code": "plugin_error", "message": str(exc), "retryable": False},
        }


def get_clicky_capabilities(args: dict, **kwargs) -> str:
    requested_platform = args.get("platform", "auto")
    platform_name = _platform_name(requested_platform)

    bridge_result = _bridge_or_error("clicky.getCapabilities", {"platform": requested_platform})
    if bridge_result.get("ok") is not False:
        return _json(bridge_result)

    # deterministic skeleton response until the native bridge exists
    return _json({
        "ok": True,
        "status": "bridge_unavailable",
        "plugin": {
            "name": "hermes-clicky-plugin",
            "version": "0.1.0",
            "folderBoundary": "all Hermes plugin code lives under hermes-clicky-plugin/",
        },
        "platform": platform_name,
        "capabilities": {
            "observeScreen": {"enabled": False, "reason": "native_bridge_unavailable"},
            "explainScreen": {"enabled": False, "reason": "native_bridge_unavailable"},
            "pointToTarget": {"enabled": False, "reason": "native_bridge_unavailable"},
            "overlay": {"enabled": False, "reason": "native_bridge_unavailable"},
            "osControl": {"enabled": False, "reason": "phase_2_not_implemented"},
        },
        "bridge": bridge_result["error"],
    })


def observe_clicky_screen(args: dict, **kwargs) -> str:
    image_mode = args.get("imageMode", "metadataOnly")
    params = {
        "imageMode": image_mode,
        "includeCursorScreenOnly": bool(args.get("includeCursorScreenOnly", True)),
    }
    result = _bridge_or_error("clicky.observeScreen", params)
    return _json(result)


def explain_clicky_screen(args: dict, **kwargs) -> str:
    task = str(args.get("task", "")).strip()
    if not task:
        return _json({"ok": False, "status": "invalid_request", "error": {"code": "missing_task", "message": "task is required", "retryable": False}})
    result = _bridge_or_error("clicky.explainScreen", {"task": task, "screenId": args.get("screenId")})
    return _json(result)


def point_clicky_target(args: dict, **kwargs) -> str:
    target = str(args.get("target", "")).strip()
    if not target:
        return _json({"ok": False, "status": "invalid_request", "error": {"code": "missing_target", "message": "target is required", "retryable": False}})
    result = _bridge_or_error("clicky.pointToTarget", {
        "target": target,
        "task": args.get("task"),
        "screenId": args.get("screenId"),
        "renderOverlay": bool(args.get("renderOverlay", True)),
    })
    return _json(result)

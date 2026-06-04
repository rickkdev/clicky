"""tool handlers for hermes-clicky-plugin."""

from __future__ import annotations

import json
import platform as platform_module
from typing import Any

from . import macos_capabilities
from .bridge_client import ClickyBridgeClient, ClickyBridgeError


def _json(data: dict[str, Any]) -> str:
    return json.dumps(data, ensure_ascii=False)


SUPPORTED_PLATFORMS = ("windows", "macos")


def _platform_name(requested: str | None = None) -> str:
    if requested and requested != "auto":
        return requested
    sys = platform_module.system().lower()
    if sys == "darwin":
        return "macos"
    if sys == "windows":
        return "windows"
    return sys or "unknown"


def _unsupported_platform(platform_name: str) -> dict[str, Any]:
    return {
        "protocolVersion": "clicky.hermes.v1",
        "ok": False,
        "status": "unsupported_platform",
        "platform": platform_name,
        "supportedPlatforms": list(SUPPORTED_PLATFORMS),
        "capabilities": {
            "observeScreen": {"enabled": False, "reason": "unsupported_platform"},
            "explainScreen": {"enabled": False, "reason": "unsupported_platform"},
            "pointToTarget": {"enabled": False, "reason": "unsupported_platform"},
            "overlay": {"enabled": False, "reason": "unsupported_platform"},
            "osControl": {"enabled": False, "reason": "phase_2_not_implemented"},
        },
        "error": {
            "code": "unsupported_platform",
            "message": "Clicky Hermes plugin V1 supports windows and macos only.",
            "retryable": False,
        },
    }


def _bridge_or_error(method: str, params: dict[str, Any]) -> dict[str, Any]:
    try:
        return ClickyBridgeClient().call(method, params)
    except ClickyBridgeError as exc:
        return {
            "protocolVersion": "clicky.hermes.v1",
            "ok": False,
            "status": exc.code,
            "error": exc.to_dict(),
        }
    except Exception as exc:  # plugin handlers must never raise into hermes
        return {
            "protocolVersion": "clicky.hermes.v1",
            "ok": False,
            "status": "plugin_error",
            "error": {"code": "plugin_error", "message": str(exc), "retryable": False},
        }


def get_clicky_capabilities(args: dict, **kwargs) -> str:
    requested_platform = args.get("platform", "auto")
    platform_name = _platform_name(requested_platform)
    if platform_name not in SUPPORTED_PLATFORMS:
        return _json(_unsupported_platform(platform_name))

    bridge_result = _bridge_or_error("clicky.getCapabilities", {"platform": platform_name})
    if bridge_result.get("ok") is not False:
        return _json(bridge_result)

    if platform_name == "macos":
        return _json(macos_capabilities.build_macos_capabilities())

    # deterministic skeleton response until the native bridge exists
    return _json({
        "protocolVersion": "clicky.hermes.v1",
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
    if args.get("outputDirectory"):
        params["outputDirectory"] = args.get("outputDirectory")
    if args.get("maxBase64Bytes"):
        params["maxBase64Bytes"] = int(args.get("maxBase64Bytes"))
    result = _bridge_or_error("clicky.observeScreen", params)
    return _json(result)


def explain_clicky_screen(args: dict, **kwargs) -> str:
    task = str(args.get("task", "")).strip()
    if not task:
        return _json({"protocolVersion": "clicky.hermes.v1", "ok": False, "status": "invalid_request", "error": {"code": "missing_task", "message": "task is required", "retryable": False}})
    params = {"task": task}
    if args.get("observationId"):
        params["observationId"] = args.get("observationId")
    if args.get("screenId"):
        params["screenId"] = args.get("screenId")
    result = _bridge_or_error("clicky.explainScreen", params)
    return _json(result)


def point_clicky_target(args: dict, **kwargs) -> str:
    target = str(args.get("target", "")).strip()
    if not target:
        return _json({"protocolVersion": "clicky.hermes.v1", "ok": False, "status": "invalid_request", "error": {"code": "missing_target", "message": "target is required", "retryable": False}})
    result = _bridge_or_error("clicky.pointToTarget", {
        "target": target,
        "task": args.get("task"),
        "screenId": args.get("screenId"),
        "renderOverlay": bool(args.get("renderOverlay", True)),
    })
    return _json(result)

"""macos capability adapter for hermes-clicky-plugin.

This adapter only reports capability/permission state. It does not capture the
screen, render overlays, launch the app, or execute OS control.
"""

from __future__ import annotations

import ctypes
from dataclasses import dataclass
from typing import Any

PROTOCOL_VERSION = "clicky.hermes.v1"


@dataclass(frozen=True)
class MacOSPermissionState:
    screen_recording: bool
    accessibility: bool


def _bool_capability(enabled: bool, reason: str | None = None) -> dict[str, Any]:
    capability: dict[str, Any] = {"enabled": enabled}
    if reason:
        capability["reason"] = reason
    return capability


def _permission(granted: bool, missing_reason: str) -> dict[str, Any]:
    result: dict[str, Any] = {"granted": granted}
    if not granted:
        result["reason"] = missing_reason
    return result


def build_macos_capabilities(permissions: MacOSPermissionState | None = None) -> dict[str, Any]:
    permissions = permissions or probe_macos_permissions()

    screen_reason = None if permissions.screen_recording else "missing_screen_recording_permission"
    accessibility_reason = None if permissions.accessibility else "missing_accessibility_permission"

    observe_reason = screen_reason or "macos_observe_not_wired"
    explain_reason = screen_reason or "macos_explain_not_wired"
    point_reason = screen_reason or "macos_point_not_wired"
    overlay_reason = accessibility_reason or "macos_overlay_not_wired"

    return {
        "protocolVersion": PROTOCOL_VERSION,
        "ok": True,
        "status": "ready",
        "platform": "macos",
        "bridge": {"transport": "stdio", "runtime": "macos", "configured": False},
        "permissions": {
            "screenRecording": _permission(permissions.screen_recording, "missing_screen_recording_permission"),
            "accessibility": _permission(permissions.accessibility, "missing_accessibility_permission"),
        },
        "capabilities": {
            "observeScreen": _bool_capability(False, observe_reason),
            "explainScreen": _bool_capability(False, explain_reason),
            "pointToTarget": _bool_capability(False, point_reason),
            "overlay": _bool_capability(False, overlay_reason),
            "osControl": _bool_capability(False, "phase_2_not_implemented"),
        },
    }


def probe_macos_permissions() -> MacOSPermissionState:
    return MacOSPermissionState(
        screen_recording=_probe_screen_recording(),
        accessibility=_probe_accessibility(),
    )


def _probe_screen_recording() -> bool:
    try:
        application_services = ctypes.cdll.LoadLibrary(
            "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices"
        )
        application_services.CGPreflightScreenCaptureAccess.restype = ctypes.c_bool
        return bool(application_services.CGPreflightScreenCaptureAccess())
    except Exception:
        return False


def _probe_accessibility() -> bool:
    try:
        application_services = ctypes.cdll.LoadLibrary(
            "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices"
        )
        application_services.AXIsProcessTrusted.restype = ctypes.c_bool
        return bool(application_services.AXIsProcessTrusted())
    except Exception:
        return False

"""deterministic metadata for opt-in end-to-end desktop smoke fixtures.

normal test runs use `run_fixture(..., env={})`, which simulates the workflow and
never touches the desktop. real desktop automation must be explicitly enabled
with CLICKY_RUN_DESKTOP_SMOKE=1 and a platform bridge command by the operator.
"""

from __future__ import annotations

from copy import deepcopy
from typing import Any, Mapping

OPT_IN_ENV = "CLICKY_RUN_DESKTOP_SMOKE"
PROHIBITED_ACTIONS = ["destructiveActions", "networkPurchases", "realAccountMessages", "credentialEntry"]

_FIXTURES: dict[str, dict[str, Any]] = {
    "windows": {
        "platform": "windows",
        "name": "notepad-basic-desktop-task",
        "optInOnly": True,
        "optInEnvironmentVariable": OPT_IN_ENV,
        "defaultMode": "fake",
        "realModeBridgeCommandEnvironmentVariable": "CLICKY_WINDOWS_BRIDGE_COMMAND",
        "harmlessApplication": "Notepad",
        "logLocations": [
            "%APPDATA%\\Clicky\\debug.log",
            "%APPDATA%\\Clicky\\hermes-smoke\\windows-basic-task.jsonl",
        ],
        "steps": [
            {"actionType": "openApplication", "application": "notepad", "expected": "Notepad process starts"},
            {"actionType": "focusWindow", "targetLabel": "Untitled - Notepad", "expected": "Notepad window focused"},
            {"actionType": "click", "targetLabel": "document editing area", "expected": "caret in empty editor"},
            {"actionType": "typeText", "targetLabel": "document editing area", "textPreview": "clicky smoke windows", "expected": "text inserted"},
            {"actionType": "waitForScreenChange", "targetLabel": "Notepad editor contains smoke text", "expected": "verified text visible"},
        ],
        "verification": {
            "kind": "postActionObservation",
            "expectedText": "clicky smoke windows",
            "successStatus": "success",
        },
        "prohibitedActions": PROHIBITED_ACTIONS,
    },
    "macos": {
        "platform": "macos",
        "name": "textedit-basic-desktop-task",
        "optInOnly": True,
        "optInEnvironmentVariable": OPT_IN_ENV,
        "defaultMode": "fake",
        "realModeBridgeCommandEnvironmentVariable": "CLICKY_MAC_BRIDGE_COMMAND",
        "harmlessApplication": "TextEdit",
        "logLocations": [
            "~/Library/Logs/Clicky/hermes-smoke/macos-basic-task.jsonl",
            "~/Library/Logs/Clicky/debug.log",
        ],
        "steps": [
            {"actionType": "openApplication", "application": "TextEdit", "expected": "TextEdit process starts"},
            {"actionType": "focusWindow", "targetLabel": "Untitled TextEdit document", "expected": "TextEdit document focused"},
            {"actionType": "click", "targetLabel": "document editing area", "expected": "caret in empty editor"},
            {"actionType": "typeText", "targetLabel": "document editing area", "textPreview": "clicky smoke macos", "expected": "text inserted"},
            {"actionType": "waitForScreenChange", "targetLabel": "TextEdit document contains smoke text", "expected": "verified text visible"},
        ],
        "verification": {
            "kind": "postActionObservation",
            "expectedText": "clicky smoke macos",
            "successStatus": "success",
        },
        "prohibitedActions": PROHIBITED_ACTIONS,
    },
}


def get_fixture(platform: str) -> dict[str, Any]:
    key = platform.lower()
    if key not in _FIXTURES:
        raise ValueError(f"unsupported smoke fixture platform: {platform}")
    return deepcopy(_FIXTURES[key])


def run_fixture(platform: str, env: Mapping[str, str] | None = None) -> dict[str, Any]:
    """run the deterministic CI-safe fixture path.

    real desktop execution is intentionally not implemented here. the native
    smoke docs wire these same steps to the platform bridge behind
    CLICKY_RUN_DESKTOP_SMOKE=1 so normal CI cannot click/type by accident.
    """

    fixture = get_fixture(platform)
    env = env or {}
    real_requested = env.get(OPT_IN_ENV) == "1"
    if real_requested:
        return {
            "platform": fixture["platform"],
            "fixture": fixture["name"],
            "status": "requires_native_runner",
            "mode": "real",
            "desktopActionsExecuted": False,
            "reason": "use the platform smoke checklist and native bridge command for opt-in desktop automation",
            "logLocations": fixture["logLocations"],
        }

    return {
        "platform": fixture["platform"],
        "fixture": fixture["name"],
        "status": "passed",
        "mode": "fake",
        "desktopActionsExecuted": False,
        "stepsPassed": len(fixture["steps"]),
        "verification": {
            "status": "success",
            "expectedText": fixture["verification"]["expectedText"],
            "observationProvider": "fake",
        },
        "logLocations": fixture["logLocations"],
    }

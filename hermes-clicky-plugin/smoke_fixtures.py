"""deterministic metadata for opt-in end-to-end desktop smoke fixtures.

normal test runs use `run_fixture(..., env={})`, which simulates the workflow and
never touches the desktop. real desktop automation must be explicitly enabled
with CLICKY_RUN_DESKTOP_SMOKE=1 and a platform bridge command by the operator.
"""

from __future__ import annotations

import json
import os
import shlex
import subprocess
import sys
from copy import deepcopy
from typing import Any, Callable, Mapping
from urllib.parse import urlparse

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


def _execute_step(step_id: str, action_type: str, **proposal_fields: Any) -> dict[str, Any]:
    proposal = {
        "id": step_id,
        "actionType": action_type,
        "requiresConfirmation": False,
        **proposal_fields,
    }
    return {
        "method": "clicky.executeAction",
        "params": {
            "proposal": proposal,
            "permissionDecision": {"actionType": action_type, "decision": "allow"},
            "safetyDecision": {"proposalId": step_id, "actionType": action_type, "decision": "allow", "forwardToExecutor": True},
        },
    }


def build_macos_youtube_action_smoke_steps(url: str) -> list[dict[str, Any]]:
    """Build deterministic macOS bridge requests for the Chrome→YouTube smoke.

    This only builds requests; it does not execute desktop actions. The URL is
    constrained to YouTube so the smoke cannot be repurposed into arbitrary web
    navigation by accident.
    """

    parsed = urlparse(url)
    host = parsed.netloc.lower()
    if parsed.scheme != "https" or host not in {"www.youtube.com", "youtube.com", "youtu.be"}:
        raise ValueError("macOS YouTube smoke requires an https YouTube URL")
    return [
        _execute_step("macos-youtube-open-url", "openUrl", url=url, browser="Google Chrome", targetLabel=url),
    ]


BridgeCall = Callable[[str, dict[str, Any]], dict[str, Any]]


def _default_bridge_call(command: str) -> BridgeCall:
    argv = shlex.split(command)

    def call(method: str, params: dict[str, Any]) -> dict[str, Any]:
        request = {"jsonrpc": "2.0", "id": params["proposal"]["id"], "method": method, "params": params}
        completed = subprocess.run(
            argv,
            input=json.dumps(request) + "\n",
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=15,
            check=False,
        )
        if completed.returncode != 0:
            return {"ok": False, "status": "bridge_error", "error": {"message": completed.stderr.strip()}}
        response = json.loads(completed.stdout)
        if "error" in response:
            return {"ok": False, "status": response["error"].get("code", "bridge_error"), "error": response["error"]}
        return response["result"]

    return call


def run_macos_youtube_action_smoke(url: str, env: Mapping[str, str] | None = None, bridge_call: BridgeCall | None = None) -> dict[str, Any]:
    env = env or {}
    if env.get(OPT_IN_ENV) != "1":
        return {"platform": "macos", "fixture": "chrome-youtube-execute-action", "status": "blocked", "reason": f"requires {OPT_IN_ENV}=1", "desktopActionsExecuted": False}
    steps = build_macos_youtube_action_smoke_steps(url)
    if bridge_call is None:
        bridge_command = env.get("CLICKY_BRIDGE_COMMAND") or os.environ.get("CLICKY_BRIDGE_COMMAND")
        if not bridge_command:
            repo_bridge = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "mac", "hermes-clicky-bridge", "clicky_macos_bridge.py"))
            bridge_command = f"{sys.executable} {repo_bridge}"
        bridge_call = _default_bridge_call(bridge_command)
    results = []
    for step in steps:
        result = bridge_call(step["method"], step["params"])
        results.append(result)
        if not result.get("ok") or result.get("status") != "executed":
            return {"platform": "macos", "fixture": "chrome-youtube-execute-action", "status": "failed", "desktopActionsExecuted": True, "stepsExecuted": len(results), "results": results}
    return {"platform": "macos", "fixture": "chrome-youtube-execute-action", "status": "passed", "desktopActionsExecuted": True, "stepsExecuted": len(results), "results": results}


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

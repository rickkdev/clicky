import json
import os
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
MAC_BRIDGE = REPO / "mac" / "hermes-clicky-bridge" / "clicky_macos_bridge.py"


def jsonrpc_request(method, params=None, request_id="mac-test-1"):
    return {
        "jsonrpc": "2.0",
        "id": request_id,
        "method": method,
        "params": params or {},
    }


class MacOSBridgeStdioTests(unittest.TestCase):
    def bridge_roundtrip(self, method, params=None, env=None):
        bridge_env = os.environ.copy()
        bridge_env.update(env or {})
        completed = subprocess.run(
            [sys.executable, str(MAC_BRIDGE)],
            input=json.dumps(jsonrpc_request(method, params)) + "\n",
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=3,
            check=False,
            env=bridge_env,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)
        lines = [line for line in completed.stdout.splitlines() if line.strip()]
        self.assertEqual(len(lines), 1, completed.stdout)
        return json.loads(lines[0])

    def test_get_capabilities_reports_macos_permissions_independently(self):
        response = self.bridge_roundtrip(
            "clicky.getCapabilities",
            {"platform": "macos"},
            env={
                "CLICKY_MAC_BRIDGE_SCREEN_RECORDING": "0",
                "CLICKY_MAC_BRIDGE_ACCESSIBILITY": "1",
            },
        )

        result = response["result"]
        self.assertEqual(result["platform"], "macos")
        self.assertFalse(result["permissions"]["screenRecording"]["granted"])
        self.assertTrue(result["permissions"]["accessibility"]["granted"])
        self.assertEqual(result["capabilities"]["observeScreen"]["reason"], "missing_screen_recording_permission")
        self.assertTrue(result["capabilities"]["osControl"]["enabled"])

    def test_observe_screen_returns_shared_protocol_shape_with_coordinate_metadata(self):
        response = self.bridge_roundtrip(
            "clicky.observeScreen",
            {"imageMode": "metadataOnly"},
            env={"CLICKY_MAC_BRIDGE_SCREEN_RECORDING": "1"},
        )

        result = response["result"]
        self.assertTrue(result["ok"])
        self.assertEqual(result["status"], "observed")
        self.assertEqual(result["platform"], "macos")
        self.assertEqual(result["coordinateSpace"], "physical_pixels")
        display = result["displays"][0]
        self.assertEqual(display["id"], "macos-display-1")
        self.assertIn("coordinateScale", display)
        self.assertEqual(result["images"][0]["mode"], "metadataOnly")
        self.assertNotIn("data", result["images"][0])
        self.assertNotIn("path", result["images"][0])

    def test_observe_screen_permission_denied_returns_structured_error(self):
        response = self.bridge_roundtrip(
            "clicky.observeScreen",
            {"imageMode": "metadataOnly"},
            env={"CLICKY_MAC_BRIDGE_SCREEN_RECORDING": "0"},
        )

        error = response["error"]
        self.assertEqual(error["code"], "permission_denied")
        self.assertEqual(error["permission"], "screen_capture")
        self.assertFalse(error["retryable"])

    def test_explain_screen_returns_semantic_response_without_provider_payloads(self):
        response = self.bridge_roundtrip(
            "clicky.explainScreen",
            {"task": "explain visible dialog", "observationId": "obs-1"},
            env={
                "CLICKY_MAC_BRIDGE_SCREEN_RECORDING": "1",
                "CLICKY_MAC_BRIDGE_EXPLANATION_RESPONSE": json.dumps({
                    "explanation": "The dialog asks for confirmation.",
                    "regions": [{"label": "confirm button", "normalized": {"x": 0.8, "y": 0.75}, "confidence": 0.9}],
                    "rawProviderPayload": {"provider": "should-not-leak"},
                }),
            },
        )

        result = response["result"]
        self.assertTrue(result["ok"])
        self.assertEqual(result["status"], "explained")
        self.assertEqual(result["explanation"], "The dialog asks for confirmation.")
        self.assertEqual(result["regions"][0]["label"], "confirm button")
        self.assertNotIn("rawProviderPayload", result)
        self.assertNotIn("provider", json.dumps(result).lower())

    def test_point_to_target_returns_protocol_coordinates_and_no_fabricated_overlay(self):
        response = self.bridge_roundtrip(
            "clicky.pointToTarget",
            {"target": "confirm button", "renderOverlay": False},
            env={
                "CLICKY_MAC_BRIDGE_SCREEN_RECORDING": "1",
                "CLICKY_MAC_BRIDGE_ACCESSIBILITY": "1",
                "CLICKY_MAC_BRIDGE_POINT_RESPONSE": json.dumps({
                    "label": "confirm button",
                    "confidence": 0.87,
                    "normalized": {"x": 0.8, "y": 0.75},
                    "physical": {"x": 1440, "y": 810, "screen": "macos-display-1"},
                    "reasoning": "button is in the lower right",
                }),
            },
        )

        result = response["result"]
        self.assertTrue(result["ok"])
        self.assertEqual(result["status"], "pointed")
        self.assertEqual(result["target"], "confirm button")
        self.assertEqual(result["normalized"], {"x": 0.8, "y": 0.75})
        self.assertEqual(result["physical"]["screen"], "macos-display-1")
        self.assertFalse(result["overlayRendered"])

    def test_execute_action_permission_denied_without_accessibility(self):
        response = self.bridge_roundtrip(
            "clicky.executeAction",
            execution_request("click"),
            env={"CLICKY_MAC_BRIDGE_ACCESSIBILITY": "0", "CLICKY_MAC_BRIDGE_FAKE_EXECUTOR": "1"},
        )

        error = response["error"]
        self.assertEqual(error["code"], "permission_denied")
        self.assertEqual(error["permission"], "accessibility")
        self.assertFalse(error["retryable"])

    def test_execute_action_refuses_missing_permission_decision_before_executor(self):
        request = execution_request("click")
        request["permissionDecision"] = None

        response = self.bridge_roundtrip(
            "clicky.executeAction",
            request,
            env={"CLICKY_MAC_BRIDGE_ACCESSIBILITY": "1", "CLICKY_MAC_BRIDGE_FAKE_EXECUTOR": "1"},
        )

        result = response["result"]
        self.assertFalse(result["ok"])
        self.assertEqual(result["status"], "blocked")
        self.assertEqual(result["reason"], "missing permission decision")
        self.assertFalse(result["forwardedToExecutor"])

    def test_execute_action_refuses_unapproved_confirmation_when_required(self):
        request = execution_request("typeText", permission_decision="requireConfirmation", requires_confirmation=True)

        response = self.bridge_roundtrip(
            "clicky.executeAction",
            request,
            env={"CLICKY_MAC_BRIDGE_ACCESSIBILITY": "1", "CLICKY_MAC_BRIDGE_FAKE_EXECUTOR": "1"},
        )

        result = response["result"]
        self.assertFalse(result["ok"])
        self.assertEqual(result["status"], "blocked")
        self.assertEqual(result["reason"], "approved confirmation required")
        self.assertFalse(result["forwardedToExecutor"])

    def test_execute_action_routes_allowed_actions_through_fake_executor(self):
        for action_type in ["click", "doubleClick", "typeText", "hotkey", "openApplication", "focusWindow"]:
            with self.subTest(action_type=action_type):
                response = self.bridge_roundtrip(
                    "clicky.executeAction",
                    execution_request(action_type),
                    env={"CLICKY_MAC_BRIDGE_ACCESSIBILITY": "1", "CLICKY_MAC_BRIDGE_FAKE_EXECUTOR": "1"},
                )

                result = response["result"]
                self.assertTrue(result["ok"])
                self.assertEqual(result["status"], "executed")
                self.assertEqual(result["actionType"], action_type)
                self.assertTrue(result["forwardedToExecutor"])
                self.assertEqual(result["result"]["method"], action_type)
                self.assertNotIn("inputPreview", json.dumps(result))
                self.assertNotIn("typed text", json.dumps(result).lower())


def execution_request(action_type, permission_decision="allow", requires_confirmation=False):
    proposal = {
        "id": f"proposal-{action_type}",
        "actionType": action_type,
        "targetLabel": "safe target",
        "confidence": 0.99,
        "riskLevel": "low",
        "requiresConfirmation": requires_confirmation,
        "rationale": "deterministic test proposal",
        "coordinates": {"x": 100, "y": 200, "screen": "macos-display-1"},
    }
    if action_type == "typeText":
        proposal["inputPreview"] = "typed text"
    if action_type == "hotkey":
        proposal["hotkey"] = ["Command", "L"]
    if action_type == "openApplication":
        proposal["application"] = "TextEdit"
    if action_type == "focusWindow":
        proposal["nativeSelector"] = {"kind": "windowTitle", "value": "Untitled"}
    return {
        "proposal": proposal,
        "permissionDecision": {
            "decision": permission_decision,
            "permissionTier": "confirmBeforeAction",
            "actionType": action_type,
            "reason": "test permission",
        },
        "safetyDecision": {
            "proposalId": proposal["id"],
            "actionType": action_type,
            "decision": "allow",
            "reason": "test safety",
            "riskFlags": [],
            "forwardToExecutor": True,
        },
        "confirmationResponse": None,
    }


if __name__ == "__main__":
    unittest.main()

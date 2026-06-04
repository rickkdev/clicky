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
        self.assertEqual(result["capabilities"]["osControl"]["reason"], "phase_2_not_implemented")

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


if __name__ == "__main__":
    unittest.main()

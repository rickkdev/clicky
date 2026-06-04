import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FAKE_BRIDGE = ROOT / "scripts" / "fake_bridge.py"


def load_bridge_client():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin.bridge_client",
        ROOT / "bridge_client.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def jsonrpc_request(method, params=None, request_id="test-1"):
    return {
        "jsonrpc": "2.0",
        "id": request_id,
        "method": method,
        "params": params or {},
    }


class ClickyBridgeClientStdioTests(unittest.TestCase):
    def setUp(self):
        self.bridge_client = load_bridge_client()

    def client(self, command=None, timeout=2):
        command = command or f"{sys.executable} {FAKE_BRIDGE}"
        return self.bridge_client.ClickyBridgeClient(
            self.bridge_client.BridgeConfig(command=command, timeout_seconds=timeout)
        )

    def test_sends_jsonrpc_request_to_fake_stdio_bridge_and_parses_result(self):
        result = self.client().call("clicky.getCapabilities", {"platform": "windows"})

        self.assertEqual(result["protocolVersion"], "clicky.hermes.v1")
        self.assertTrue(result["ok"])
        self.assertEqual(result["status"], "ready")
        self.assertEqual(result["platform"], "windows")
        self.assertTrue(result["capabilities"]["observeScreen"]["enabled"])

    def test_jsonrpc_error_object_raises_clicky_bridge_error_with_structured_fields(self):
        with self.assertRaises(self.bridge_client.ClickyBridgeError) as raised:
            self.client().call("clicky.forcePermissionDenied", {})

        error = raised.exception
        self.assertEqual(error.code, "permission_denied")
        self.assertEqual(error.permission, "screen_recording")
        self.assertFalse(error.retryable)
        self.assertEqual(error.to_dict()["permission"], "screen_recording")

    def test_timeout_is_structured_retryable_error(self):
        command = f"{sys.executable} -c 'import time; time.sleep(2)'"
        with self.assertRaises(self.bridge_client.ClickyBridgeError) as raised:
            self.client(command=command, timeout=1).call("rpc.health", {})

        self.assertEqual(raised.exception.code, "bridge_timeout")
        self.assertTrue(raised.exception.retryable)

    def test_empty_output_is_structured_retryable_error(self):
        command = f"{sys.executable} -c 'pass'"
        with self.assertRaises(self.bridge_client.ClickyBridgeError) as raised:
            self.client(command=command).call("rpc.health", {})

        self.assertEqual(raised.exception.code, "bridge_empty_response")
        self.assertTrue(raised.exception.retryable)

    def test_invalid_json_is_structured_retryable_error(self):
        command = f"{sys.executable} -c 'print(\"not-json\")'"
        with self.assertRaises(self.bridge_client.ClickyBridgeError) as raised:
            self.client(command=command).call("rpc.health", {})

        self.assertEqual(raised.exception.code, "bridge_invalid_json")
        self.assertTrue(raised.exception.retryable)


class FakeBridgeRouterTests(unittest.TestCase):
    def bridge_roundtrip(self, method, params=None):
        completed = subprocess.run(
            [sys.executable, str(FAKE_BRIDGE)],
            input=json.dumps(jsonrpc_request(method, params)) + "\n",
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=2,
            check=False,
        )
        self.assertEqual(completed.returncode, 0, completed.stderr)
        lines = [line for line in completed.stdout.splitlines() if line.strip()]
        self.assertEqual(len(lines), 1, completed.stdout)
        return json.loads(lines[0])

    def test_health_request_starts_returns_structured_response_and_exits_cleanly(self):
        response = self.bridge_roundtrip("rpc.health")

        self.assertEqual(response["jsonrpc"], "2.0")
        self.assertEqual(response["id"], "test-1")
        self.assertEqual(response["result"]["status"], "ok")
        self.assertEqual(response["result"]["transport"], "stdio")

    def test_clicky_get_capabilities_route_works(self):
        response = self.bridge_roundtrip("clicky.getCapabilities", {"platform": "windows"})

        result = response["result"]
        self.assertTrue(result["ok"])
        self.assertEqual(result["platform"], "windows")
        self.assertFalse(result["capabilities"]["osControl"]["enabled"])

    def test_clicky_observe_screen_route_works(self):
        response = self.bridge_roundtrip("clicky.observeScreen", {"imageMode": "metadataOnly"})

        result = response["result"]
        self.assertTrue(result["ok"])
        self.assertEqual(result["status"], "observed")
        self.assertEqual(result["images"], [])
        self.assertEqual(result["coordinateSpace"]["kind"], "normalized")

    def test_clicky_explain_screen_route_works(self):
        response = self.bridge_roundtrip("clicky.explainScreen", {"task": "explain visible controls"})

        result = response["result"]
        self.assertTrue(result["ok"])
        self.assertIn("fake bridge", result["explanation"])

    def test_clicky_point_to_target_route_works(self):
        response = self.bridge_roundtrip("clicky.pointToTarget", {"target": "settings", "renderOverlay": False})

        result = response["result"]
        self.assertTrue(result["ok"])
        self.assertEqual(result["status"], "pointed")
        self.assertEqual(result["target"], "settings")
        self.assertFalse(result["overlayRendered"])

    def test_unknown_method_returns_structured_protocol_error(self):
        response = self.bridge_roundtrip("clicky.nope")

        error = response["error"]
        self.assertEqual(error["code"], "method_not_found")
        self.assertFalse(error["retryable"])
        self.assertIn("permission", error)


if __name__ == "__main__":
    unittest.main()

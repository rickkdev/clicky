import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_plugin_package():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin_execute_tests",
        ROOT / "__init__.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class ExecuteClickyActionWrapperTests(unittest.TestCase):
    def setUp(self):
        self.plugin = load_plugin_package()
        self.captured = {}
        self.bridge_called = False

        def fake_bridge(method, params):
            self.bridge_called = True
            self.captured["method"] = method
            self.captured["params"] = params
            return {
                "protocolVersion": "clicky.hermes.v1",
                "ok": True,
                "status": "executed",
                "actionType": params["proposal"]["actionType"],
            }

        self.plugin.tools._bridge_or_error = fake_bridge

    def execute(self, args):
        result = json.loads(self.plugin.tools.execute_clicky_action(args))
        self.assertTrue(result["ok"], result)
        self.assertTrue(self.bridge_called)
        self.assertEqual(self.captured["method"], "clicky.executeAction")
        return self.captured["params"]

    def assert_common_native_gate_params(self, params, action_type):
        proposal_id = params["proposal"]["id"]
        self.assertEqual(params["proposal"]["actionType"], action_type)
        self.assertEqual(params["permissionDecision"], {"actionType": action_type, "decision": "allow"})
        self.assertEqual(
            params["safetyDecision"],
            {
                "proposalId": proposal_id,
                "actionType": action_type,
                "decision": "allow",
                "forwardToExecutor": True,
            },
        )

    def test_open_application_builds_native_proposal_application_and_target_label(self):
        params = self.execute({"actionType": "openApplication", "target": "Google Chrome"})

        self.assert_common_native_gate_params(params, "openApplication")
        self.assertEqual(params["proposal"]["application"], "Google Chrome")
        self.assertEqual(params["proposal"]["targetLabel"], "Google Chrome")

    def test_type_text_builds_native_proposal_input_preview(self):
        params = self.execute({"actionType": "typeText", "text": "hello world"})

        self.assert_common_native_gate_params(params, "typeText")
        self.assertEqual(params["proposal"]["inputPreview"], "hello world")

    def test_hotkey_builds_native_proposal_hotkey(self):
        params = self.execute({"actionType": "hotkey", "keys": ["cmd", "l"]})

        self.assert_common_native_gate_params(params, "hotkey")
        self.assertEqual(params["proposal"]["hotkey"], ["cmd", "l"])

    def test_click_builds_native_proposal_coordinates(self):
        position = {"x": 100, "y": 200, "displayId": "main"}
        params = self.execute({"actionType": "click", "position": position})

        self.assert_common_native_gate_params(params, "click")
        self.assertEqual(params["proposal"]["coordinates"], position)

    def test_confirmation_approved_builds_native_confirmation_response(self):
        params = self.execute({
            "actionType": "openApplication",
            "target": "Calendar",
            "requiresConfirmation": True,
            "confirmationApproved": True,
        })

        proposal_id = params["proposal"]["id"]
        self.assertEqual(
            params["confirmationResponse"],
            {
                "proposalId": proposal_id,
                "decision": "approved",
                "approved": True,
                "forwardToExecutor": True,
                "safetyDecision": {"decision": "allow", "forwardToExecutor": True},
            },
        )

    def test_invalid_action_type_is_blocked_before_bridge(self):
        result = json.loads(self.plugin.tools.execute_clicky_action({"actionType": "launchNukes"}))

        self.assertFalse(self.bridge_called)
        self.assertFalse(result["ok"])
        self.assertEqual(result["status"], "invalid_request")
        self.assertEqual(result["error"]["code"], "invalid_action_type")

    def test_missing_action_type_is_blocked_before_bridge(self):
        result = json.loads(self.plugin.tools.execute_clicky_action({"target": "Google Chrome"}))

        self.assertFalse(self.bridge_called)
        self.assertFalse(result["ok"])
        self.assertEqual(result["status"], "invalid_request")
        self.assertEqual(result["error"]["code"], "missing_action_type")

    def test_schema_accepts_hotkey_and_text_alias_fields(self):
        properties = self.plugin.schemas.EXECUTE_CLICKY_ACTION["parameters"]["properties"]

        self.assertIn("keys", properties)
        self.assertIn("hotkey", properties)
        self.assertIn("text", properties)
        self.assertIn("inputPreview", properties)
        self.assertEqual(properties["hotkey"]["type"], "array")
        self.assertEqual(properties["inputPreview"]["type"], "string")


if __name__ == "__main__":
    unittest.main()

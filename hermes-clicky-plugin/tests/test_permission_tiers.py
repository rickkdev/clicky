import importlib.util
import json
import os
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_plugin_package():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin_permission_tests",
        ROOT / "__init__.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class PermissionTierTests(unittest.TestCase):
    def setUp(self):
        os.environ.pop("CLICKY_PERMISSION_TIER", None)

    def tearDown(self):
        os.environ.pop("CLICKY_PERMISSION_TIER", None)

    def proposal(self, action_type="click", risk_level="low"):
        return {
            "id": "proposal-1",
            "actionType": action_type,
            "targetLabel": "safe button",
            "confidence": 0.92,
            "riskLevel": risk_level,
            "requiresConfirmation": True,
            "rationale": "test proposal",
        }

    def test_schema_defines_permission_tiers_and_decision_shape(self):
        schema = json.loads((ROOT / "protocol" / "schema.json").read_text(encoding="utf-8"))
        defs = schema["$defs"]

        self.assertEqual(
            defs["permissionTier"]["enum"],
            ["observe", "point", "confirmBeforeAction", "scopedAutopilot", "fullControl"],
        )
        decision = defs["permissionDecision"]
        self.assertEqual(set(decision["properties"]["decision"]["enum"]), {"allow", "requireConfirmation", "block"})
        self.assertIn("permissionTier", decision["required"])
        self.assertIn("actionType", decision["required"])
        self.assertIn("reason", decision["required"])

    def test_get_capabilities_reports_default_active_permission_tier_and_available_tiers(self):
        plugin = load_plugin_package()
        plugin.tools.platform_module.system = lambda: "Windows"
        plugin.tools._bridge_or_error = lambda method, params: {
            "protocolVersion": "clicky.hermes.v1",
            "ok": False,
            "status": "bridge_unavailable",
            "error": {"code": "bridge_unavailable", "message": "no bridge", "retryable": True},
        }

        result = json.loads(plugin.tools.get_clicky_capabilities({"platform": "auto"}))

        self.assertEqual(result["activePermissionTier"], "confirmBeforeAction")
        self.assertEqual(result["defaultPermissionTier"], "confirmBeforeAction")
        self.assertEqual(
            result["availablePermissionTiers"],
            ["observe", "point", "confirmBeforeAction", "scopedAutopilot", "fullControl"],
        )
        self.assertEqual(result["permissionTierChange"], "explicit_user_setting_or_command_required")
        self.assertFalse(result["capabilities"]["osControl"]["enabled"])

    def test_plugin_request_cannot_silently_enable_full_control(self):
        load_plugin_package()
        from hermes_clicky_plugin_permission_tests.permission_policy import resolve_active_permission_tier

        tier = resolve_active_permission_tier(requested_tier="fullControl", environ={})

        self.assertEqual(tier, "confirmBeforeAction")

    def test_full_control_can_only_come_from_external_user_configuration(self):
        load_plugin_package()
        from hermes_clicky_plugin_permission_tests.permission_policy import resolve_active_permission_tier

        tier = resolve_active_permission_tier(requested_tier=None, environ={"CLICKY_PERMISSION_TIER": "fullControl"})

        self.assertEqual(tier, "fullControl")

    def test_each_tier_allows_or_blocks_expected_action_classes(self):
        load_plugin_package()
        from hermes_clicky_plugin_permission_tests.permission_policy import evaluate_permission_for_action

        expectations = {
            "observe": {
                "click": "block",
                "typeText": "block",
                "hotkey": "block",
                "openApplication": "block",
                "focusWindow": "block",
                "waitForScreenChange": "block",
                "stop": "block",
            },
            "point": {
                "click": "block",
                "typeText": "block",
                "hotkey": "block",
                "openApplication": "block",
                "focusWindow": "block",
                "waitForScreenChange": "block",
                "stop": "block",
            },
            "confirmBeforeAction": {
                "click": "requireConfirmation",
                "typeText": "requireConfirmation",
                "hotkey": "requireConfirmation",
                "openApplication": "requireConfirmation",
                "focusWindow": "requireConfirmation",
                "waitForScreenChange": "requireConfirmation",
                "stop": "requireConfirmation",
            },
            "scopedAutopilot": {
                "click": "allow",
                "typeText": "requireConfirmation",
                "hotkey": "requireConfirmation",
                "openApplication": "requireConfirmation",
                "focusWindow": "allow",
                "waitForScreenChange": "allow",
                "stop": "allow",
            },
            "fullControl": {
                "click": "allow",
                "typeText": "allow",
                "hotkey": "allow",
                "openApplication": "allow",
                "focusWindow": "allow",
                "waitForScreenChange": "allow",
                "stop": "allow",
            },
        }

        for tier, action_expectations in expectations.items():
            for action_type, expected_decision in action_expectations.items():
                with self.subTest(tier=tier, action_type=action_type):
                    decision = evaluate_permission_for_action(tier, self.proposal(action_type=action_type))
                    self.assertEqual(decision["decision"], expected_decision)
                    self.assertEqual(decision["permissionTier"], tier)
                    self.assertEqual(decision["actionType"], action_type)
                    self.assertIn("reason", decision)

    def test_blocked_risk_level_stays_blocked_even_in_full_control(self):
        load_plugin_package()
        from hermes_clicky_plugin_permission_tests.permission_policy import evaluate_permission_for_action

        decision = evaluate_permission_for_action("fullControl", self.proposal(action_type="click", risk_level="blocked"))

        self.assertEqual(decision["decision"], "block")
        self.assertIn("blocked", decision["reason"])

    def test_permission_evaluation_returns_data_only_not_execution_result(self):
        load_plugin_package()
        from hermes_clicky_plugin_permission_tests.permission_policy import evaluate_permission_for_action

        decision = evaluate_permission_for_action("fullControl", self.proposal(action_type="click"))

        self.assertNotIn("executed", decision)
        self.assertNotIn("executionResult", decision)
        self.assertNotIn("nativeResult", decision)


if __name__ == "__main__":
    unittest.main()

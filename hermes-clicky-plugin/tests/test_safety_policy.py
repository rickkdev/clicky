import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_plugin_package():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin_safety_tests",
        ROOT / "__init__.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class SafetyPolicyTests(unittest.TestCase):
    def setUp(self):
        load_plugin_package()
        from hermes_clicky_plugin_safety_tests.safety_policy import evaluate_safety_policy

        self.evaluate_safety_policy = evaluate_safety_policy

    def proposal(self, **overrides):
        data = {
            "id": "proposal-1",
            "actionType": "click",
            "targetLabel": "Save button",
            "confidence": 0.95,
            "riskLevel": "low",
            "requiresConfirmation": True,
            "rationale": "safe navigation click",
        }
        data.update(overrides)
        return data

    def test_schema_defines_safety_decision_and_risk_flags(self):
        schema = json.loads((ROOT / "protocol" / "schema.json").read_text(encoding="utf-8"))
        defs = schema["$defs"]

        self.assertIn("safetyDecision", defs)
        safety_decision = defs["safetyDecision"]
        self.assertEqual(set(safety_decision["properties"]["decision"]["enum"]), {"allow", "requireConfirmation", "block"})
        for field in ["proposalId", "decision", "reason", "riskFlags", "forwardToExecutor"]:
            self.assertIn(field, safety_decision["required"])
        self.assertIn("safetyDecisions", defs["actionProposalsResponse"]["properties"])

    def test_low_risk_high_confidence_navigation_can_be_allowed_as_data_only(self):
        decision = self.evaluate_safety_policy(self.proposal())

        self.assertEqual(decision["decision"], "allow")
        self.assertEqual(decision["riskFlags"], [])
        self.assertTrue(decision["forwardToExecutor"])
        self.assertIn("reason", decision)
        self.assertNotIn("executed", decision)
        self.assertNotIn("nativeResult", decision)

    def test_destructive_action_is_blocked_and_logged_without_forwarding(self):
        audit_log = []
        decision = self.evaluate_safety_policy(
            self.proposal(targetLabel="Delete account button", rationale="delete the account permanently"),
            audit_log=audit_log,
        )

        self.assertEqual(decision["decision"], "block")
        self.assertIn("destructive", decision["riskFlags"])
        self.assertFalse(decision["forwardToExecutor"])
        self.assertEqual(audit_log[0]["event"], "safety_policy_decision")
        self.assertEqual(audit_log[0]["status"], "blocked")
        self.assertFalse(audit_log[0]["forwardToExecutor"])

    def test_payment_action_is_blocked(self):
        decision = self.evaluate_safety_policy(self.proposal(targetLabel="Pay invoice", rationale="submit payment"))

        self.assertEqual(decision["decision"], "block")
        self.assertIn("payment", decision["riskFlags"])
        self.assertFalse(decision["forwardToExecutor"])

    def test_purchase_action_is_blocked(self):
        decision = self.evaluate_safety_policy(self.proposal(targetLabel="Buy now", rationale="purchase this item"))

        self.assertEqual(decision["decision"], "block")
        self.assertIn("purchase", decision["riskFlags"])

    def test_sending_message_or_email_requires_confirmation(self):
        for proposal in [
            self.proposal(actionType="typeText", targetLabel="Email compose body", inputPreview="send this email to Alice"),
            self.proposal(targetLabel="Send message button", rationale="send a slack message"),
        ]:
            with self.subTest(proposal=proposal):
                decision = self.evaluate_safety_policy(proposal)
                self.assertEqual(decision["decision"], "requireConfirmation")
                self.assertIn("send_message", decision["riskFlags"])
                self.assertTrue(decision["forwardToExecutor"])

    def test_credential_entry_is_blocked(self):
        decision = self.evaluate_safety_policy(self.proposal(actionType="typeText", targetLabel="Password field", inputPreview="hunter2"))

        self.assertEqual(decision["decision"], "block")
        self.assertIn("credential_entry", decision["riskFlags"])
        self.assertFalse(decision["forwardToExecutor"])

    def test_permission_prompt_is_blocked(self):
        decision = self.evaluate_safety_policy(self.proposal(targetLabel="Allow screen recording permission", rationale="click allow on permission prompt"))

        self.assertEqual(decision["decision"], "block")
        self.assertIn("permission_prompt", decision["riskFlags"])
        self.assertFalse(decision["forwardToExecutor"])

    def test_low_confidence_target_requires_confirmation(self):
        decision = self.evaluate_safety_policy(self.proposal(confidence=0.49))

        self.assertEqual(decision["decision"], "requireConfirmation")
        self.assertIn("low_confidence", decision["riskFlags"])
        self.assertTrue(decision["forwardToExecutor"])

    def test_explicit_blocked_risk_level_blocks_even_without_keyword_match(self):
        decision = self.evaluate_safety_policy(self.proposal(riskLevel="blocked", targetLabel="unknown target"))

        self.assertEqual(decision["decision"], "block")
        self.assertIn("blocked_risk", decision["riskFlags"])
        self.assertFalse(decision["forwardToExecutor"])


if __name__ == "__main__":
    unittest.main()

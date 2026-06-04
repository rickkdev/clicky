import importlib.util
import json
import sys
import unittest
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_plugin_package():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin_confirmation_tests",
        ROOT / "__init__.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class ConfirmationStateTests(unittest.TestCase):
    def setUp(self):
        load_plugin_package()
        from hermes_clicky_plugin_confirmation_tests.confirmation_state import (
            build_confirmation_request,
            handle_confirmation_response,
        )

        self.build_confirmation_request = build_confirmation_request
        self.handle_confirmation_response = handle_confirmation_response

    def proposal(self, **overrides):
        data = {
            "id": "proposal-1",
            "actionType": "typeText",
            "targetLabel": "Email compose body",
            "confidence": 0.91,
            "riskLevel": "medium",
            "requiresConfirmation": True,
            "rationale": "Need to send message after user approval. internal chain-of-thought should not leak.",
            "inputPreview": "password=super-secret-token-123",
        }
        data.update(overrides)
        return data

    def test_schema_defines_confirmation_request_and_response_shapes(self):
        schema = json.loads((ROOT / "protocol" / "schema.json").read_text(encoding="utf-8"))
        defs = schema["$defs"]

        self.assertIn("confirmationRequest", defs)
        self.assertIn("confirmationResponse", defs)
        self.assertEqual(
            set(defs["confirmationResponse"]["properties"]["decision"]["enum"]),
            {"approved", "cancelled", "explanationRequested", "stale"},
        )
        for field in ["confirmationId", "proposalId", "actionSummary", "target", "risk", "reason", "expiresAt"]:
            self.assertIn(field, defs["confirmationRequest"]["required"])

    def test_build_confirmation_request_is_structured_concise_and_redacted(self):
        request = self.build_confirmation_request(
            self.proposal(),
            safety_decision={
                "proposalId": "proposal-1",
                "decision": "requireConfirmation",
                "reason": "requires user confirmation: send_message",
                "riskFlags": ["send_message"],
                "forwardToExecutor": True,
            },
            now="2026-06-04T18:00:00Z",
            ttl_seconds=120,
        )

        self.assertEqual(request["protocolVersion"], "clicky.hermes.v1")
        self.assertTrue(request["ok"])
        self.assertEqual(request["status"], "confirmation_required")
        self.assertEqual(request["proposalId"], "proposal-1")
        self.assertEqual(request["target"], "Email compose body")
        self.assertEqual(request["risk"], "medium")
        self.assertEqual(request["expiresAt"], "2026-06-04T18:02:00Z")
        self.assertIn("type text", request["actionSummary"].lower())
        self.assertNotIn("super-secret", json.dumps(request))
        self.assertNotIn("password", json.dumps(request).lower())
        self.assertNotIn("chain-of-thought", json.dumps(request).lower())
        self.assertLessEqual(len(request["reason"]), 96)

    def test_cancelled_confirmation_does_not_forward_and_logs_cancelled_event(self):
        audit_log = []
        request = self.build_confirmation_request(self.proposal(), now="2026-06-04T18:00:00Z", ttl_seconds=120)

        response = self.handle_confirmation_response(
            request,
            self.proposal(),
            user_decision="cancel",
            now="2026-06-04T18:01:00Z",
            audit_log=audit_log,
        )

        self.assertEqual(response["decision"], "cancelled")
        self.assertFalse(response["forwardToExecutor"])
        self.assertEqual(response["auditEvent"]["status"], "cancelled")
        self.assertEqual(audit_log[-1]["event"], "confirmation_cancelled")
        self.assertNotIn("executed", response)
        self.assertNotIn("executionResult", response)

    def test_approved_confirmation_rechecks_safety_before_forwarding(self):
        request = self.build_confirmation_request(self.proposal(actionType="click", targetLabel="Safe button", riskLevel="low", rationale="safe navigation click", inputPreview=None), now="2026-06-04T18:00:00Z")

        response = self.handle_confirmation_response(
            request,
            self.proposal(actionType="click", targetLabel="Safe button", riskLevel="low", rationale="safe navigation click", inputPreview=None),
            user_decision="approve",
            now="2026-06-04T18:00:30Z",
        )

        self.assertEqual(response["decision"], "approved")
        self.assertEqual(response["safetyDecision"]["decision"], "allow")
        self.assertTrue(response["safetyRechecked"])
        self.assertTrue(response["forwardToExecutor"])
        self.assertEqual(response["auditEvent"]["event"], "confirmation_approved")
        self.assertNotIn("nativeResult", response)

    def test_approved_confirmation_recheck_can_block_forwarding(self):
        request = self.build_confirmation_request(self.proposal(actionType="click", targetLabel="Safe button", riskLevel="low", rationale="safe navigation click"), now="2026-06-04T18:00:00Z")
        changed = self.proposal(targetLabel="Delete account button", rationale="delete account permanently", riskLevel="high")

        response = self.handle_confirmation_response(
            request,
            changed,
            user_decision="approve",
            now="2026-06-04T18:00:30Z",
        )

        self.assertEqual(response["decision"], "approved")
        self.assertEqual(response["safetyDecision"]["decision"], "block")
        self.assertTrue(response["safetyRechecked"])
        self.assertFalse(response["forwardToExecutor"])

    def test_user_can_ask_for_explanation_without_forwarding(self):
        request = self.build_confirmation_request(self.proposal(), now="2026-06-04T18:00:00Z")

        response = self.handle_confirmation_response(request, self.proposal(), user_decision="explain", now="2026-06-04T18:01:00Z")

        self.assertEqual(response["decision"], "explanationRequested")
        self.assertTrue(response["needsExplanation"])
        self.assertFalse(response["forwardToExecutor"])

    def test_stale_proposal_is_rejected_by_expiry_or_id_mismatch(self):
        request = self.build_confirmation_request(self.proposal(), now="2026-06-04T18:00:00Z", ttl_seconds=60)

        expired = self.handle_confirmation_response(request, self.proposal(), user_decision="approve", now="2026-06-04T18:02:00Z")
        mismatched = self.handle_confirmation_response(request, self.proposal(id="proposal-2"), user_decision="approve", now="2026-06-04T18:00:30Z")

        self.assertEqual(expired["decision"], "stale")
        self.assertFalse(expired["forwardToExecutor"])
        self.assertEqual(mismatched["decision"], "stale")
        self.assertEqual(mismatched["reason"], "proposal id mismatch")
        self.assertFalse(mismatched["forwardToExecutor"])


if __name__ == "__main__":
    unittest.main()

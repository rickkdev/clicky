import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_plugin_package():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin_audit_tests",
        ROOT / "__init__.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class ActionAuditLogTests(unittest.TestCase):
    def setUp(self):
        load_plugin_package()
        from hermes_clicky_plugin_audit_tests.audit_log import ActionAuditLog, default_log_locations, redact_sensitive_text

        self.ActionAuditLog = ActionAuditLog
        self.default_log_locations = default_log_locations
        self.redact_sensitive_text = redact_sensitive_text

    def proposal(self, **overrides):
        data = {
            "id": "proposal-1",
            "actionType": "typeText",
            "targetLabel": "Login form password field",
            "inputPreview": "hunter2 should never be stored in full",
            "confidence": 0.92,
        }
        data.update(overrides)
        return data

    def test_redacts_passwords_tokens_and_full_typed_content_by_default(self):
        text = "password=hunter2 api_key=sk-live-123 token: abc secret phrase"

        self.assertEqual(self.redact_sensitive_text(text), "[redacted]")

        record = self.ActionAuditLog.build_record(
            event="action_proposed",
            request_id="req-1",
            user_request_summary="type password=hunter2 into api_key=sk-live-123 field",
            proposal=self.proposal(),
            decision="requireConfirmation",
            result="pending",
            timestamp="2026-06-15T18:00:00Z",
        )
        dumped = json.dumps(record).lower()

        self.assertEqual(record["targetSummary"], "target")
        self.assertEqual(record["inputSummary"], "[redacted]")
        self.assertNotIn("hunter2", dumped)
        self.assertNotIn("sk-live", dumped)
        self.assertNotIn("api_key", dumped)
        self.assertNotIn("password=", dumped)

    def test_lifecycle_records_are_ordered_jsonl_and_support_minimized_mode(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "audit.jsonl"
            audit = self.ActionAuditLog(path=path, enabled=True)
            proposal = self.proposal(actionType="click", targetLabel="Submit button", inputPreview=None)

            audit.record_proposal("req-1", "submit the form", proposal, timestamp="2026-06-15T18:00:00Z")
            audit.record_policy_decision("req-1", proposal, "allow", timestamp="2026-06-15T18:00:01Z")
            audit.record_confirmation("req-1", proposal, "approved", timestamp="2026-06-15T18:00:02Z")
            audit.record_execution("req-1", proposal, "executed", timestamp="2026-06-15T18:00:03Z")
            audit.record_verification("req-1", proposal, "success", timestamp="2026-06-15T18:00:04Z")

            rows = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]
            self.assertEqual(
                [row["event"] for row in rows],
                [
                    "action_proposed",
                    "policy_decision",
                    "confirmation",
                    "execution",
                    "verification",
                ],
            )
            self.assertEqual([row["sequence"] for row in rows], [1, 2, 3, 4, 5])
            for row in rows:
                for field in [
                    "timestamp",
                    "requestId",
                    "userRequestSummary",
                    "actionType",
                    "targetSummary",
                    "decision",
                    "result",
                ]:
                    self.assertIn(field, row)

            minimized_path = Path(tmp) / "audit-min.jsonl"
            minimized = self.ActionAuditLog(path=minimized_path, mode="minimized")
            minimized.record_failure("req-2", proposal, "bridge_timeout", timestamp="2026-06-15T18:01:00Z")
            minimized_row = json.loads(minimized_path.read_text(encoding="utf-8"))
            self.assertEqual(minimized_row["errorCategory"], "bridge_timeout")
            self.assertNotIn("userRequestSummary", minimized_row)
            self.assertNotIn("targetSummary", minimized_row)

    def test_disabled_log_drops_records_and_documents_platform_locations(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "audit.jsonl"
            audit = self.ActionAuditLog(path=path, enabled=False)
            audit.record_cancellation("req-1", self.proposal(), timestamp="2026-06-15T18:00:00Z")

            self.assertFalse(path.exists())

        locations = self.default_log_locations()
        self.assertIn("windows", locations)
        self.assertIn("macos", locations)
        self.assertIn("Clicky", locations["windows"])
        self.assertIn("clicky", locations["macos"].lower())


if __name__ == "__main__":
    unittest.main()

import importlib.util
import sys
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_plugin_package():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin_autopilot_tests",
        ROOT / "__init__.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class ScopedAutopilotSessionTests(unittest.TestCase):
    def setUp(self):
        load_plugin_package()
        from hermes_clicky_plugin_autopilot_tests.scoped_autopilot import ScopedAutopilotSession

        self.ScopedAutopilotSession = ScopedAutopilotSession
        self.now = datetime(2026, 6, 15, 20, 0, 0, tzinfo=timezone.utc)

    def session(self, **overrides):
        data = {
            "session_id": "auto-1",
            "task_description": "organize harmless notes",
            "allowed_applications": ["Notes"],
            "allowed_windows": ["Inbox"],
            "allowed_action_types": ["click", "waitForScreenChange", "focusWindow"],
            "timeout_seconds": 60,
            "max_steps": 2,
            "started_at": self.now,
        }
        data.update(overrides)
        return self.ScopedAutopilotSession(**data)

    def proposal(self, **overrides):
        data = {
            "id": "proposal-1",
            "actionType": "click",
            "targetLabel": "archive checkbox",
            "application": "Notes",
            "windowTitle": "Inbox",
            "riskLevel": "low",
            "confidence": 0.96,
            "rationale": "select a harmless note",
        }
        data.update(overrides)
        return data

    def test_allows_low_risk_in_scope_action_and_records_session_audit(self):
        audit_events = []
        session = self.session(audit_log=audit_events)

        result = session.evaluate_next_action(self.proposal(), now=self.now + timedelta(seconds=5))

        self.assertEqual(result["status"], "allowed")
        self.assertEqual(result["sessionId"], "auto-1")
        self.assertEqual(result["stepIndex"], 1)
        self.assertEqual(result["permissionDecision"]["decision"], "allow")
        self.assertEqual(result["safetyDecision"]["decision"], "allow")
        self.assertTrue(result["forwardToExecutor"])
        self.assertEqual(audit_events[-1]["sessionId"], "auto-1")
        self.assertEqual(audit_events[-1]["event"], "autopilot_step")

    def test_stops_on_timeout_before_forwarding_action(self):
        session = self.session(timeout_seconds=10)

        result = session.evaluate_next_action(self.proposal(), now=self.now + timedelta(seconds=11))

        self.assertEqual(result["status"], "stopped")
        self.assertEqual(result["stopReason"], "timeout")
        self.assertFalse(result["forwardToExecutor"])
        self.assertTrue(session.is_stopped)

    def test_stops_on_max_steps(self):
        session = self.session(max_steps=1)
        first = session.evaluate_next_action(self.proposal(id="proposal-1"), now=self.now + timedelta(seconds=1))
        second = session.evaluate_next_action(self.proposal(id="proposal-2"), now=self.now + timedelta(seconds=2))

        self.assertEqual(first["status"], "allowed")
        self.assertEqual(second["status"], "stopped")
        self.assertEqual(second["stopReason"], "max_steps")
        self.assertFalse(second["forwardToExecutor"])

    def test_stops_on_app_or_window_scope_violation(self):
        session = self.session()

        result = session.evaluate_next_action(
            self.proposal(application="Mail", windowTitle="Inbox"),
            now=self.now + timedelta(seconds=1),
        )

        self.assertEqual(result["status"], "stopped")
        self.assertEqual(result["stopReason"], "scope_violation")
        self.assertIn("application", result["reason"])
        self.assertFalse(result["forwardToExecutor"])

    def test_high_risk_action_requires_confirmation_inside_autopilot(self):
        session = self.session(allowed_action_types=["click", "typeText"])

        result = session.evaluate_next_action(
            self.proposal(actionType="typeText", riskLevel="high", inputPreview="draft message", targetLabel="message body"),
            now=self.now + timedelta(seconds=1),
        )

        self.assertEqual(result["status"], "requiresConfirmation")
        self.assertFalse(result["forwardToExecutor"])
        self.assertEqual(result["permissionDecision"]["decision"], "requireConfirmation")

    def test_stops_on_failed_verification_and_user_cancel(self):
        session = self.session()
        failed = session.record_verification_result({"status": "failed", "reason": "target_unchanged"})

        self.assertEqual(failed["status"], "stopped")
        self.assertEqual(failed["stopReason"], "failed_verification")
        self.assertTrue(session.is_stopped)

        cancelled_session = self.session(session_id="auto-2")
        cancelled = cancelled_session.cancel(reason="user_cancel")

        self.assertEqual(cancelled["status"], "stopped")
        self.assertEqual(cancelled["stopReason"], "user_cancel")
        self.assertTrue(cancelled_session.is_stopped)


if __name__ == "__main__":
    unittest.main()

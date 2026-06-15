import importlib.util
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_plugin_package():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin_verification_tests",
        ROOT / "__init__.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class PostActionVerificationTests(unittest.TestCase):
    def setUp(self):
        load_plugin_package()
        from hermes_clicky_plugin_verification_tests.post_action_verification import (
            PostActionVerificationRunner,
            verify_post_action_observation,
        )

        self.PostActionVerificationRunner = PostActionVerificationRunner
        self.verify_post_action_observation = verify_post_action_observation

    def execution_result(self, **overrides):
        data = {
            "protocolVersion": "clicky.hermes.v1",
            "ok": True,
            "status": "executed",
            "proposalId": "proposal-1",
            "actionType": "click",
            "result": {},
            "forwardedToExecutor": True,
        }
        data.update(overrides)
        return data

    def observation(self, **overrides):
        data = {
            "screenChanged": True,
            "permissionPromptVisible": False,
            "errorDialogVisible": False,
            "targetStillUnchanged": False,
            "appLostFocus": False,
            "matchedExpectedState": True,
            "summary": "settings dialog opened",
        }
        data.update(overrides)
        return data

    def test_runner_observes_after_executed_action_and_reports_success(self):
        observed = []

        def fake_observe(execution_result, expected_state):
            observed.append((execution_result["proposalId"], expected_state))
            return self.observation()

        runner = self.PostActionVerificationRunner(fake_observe)
        result = runner.verify_after_execution(
            self.execution_result(),
            expected_state="settings dialog opened",
            request_id="req-1",
        )

        self.assertEqual(observed, [("proposal-1", "settings dialog opened")])
        self.assertEqual(result["status"], "success")
        self.assertTrue(result["continueAutomation"])
        self.assertEqual(result["proposalId"], "proposal-1")

    def test_runner_skips_observation_when_execution_does_not_need_verification(self):
        def fail_if_called(*_args, **_kwargs):
            raise AssertionError("observer should not run")

        runner = self.PostActionVerificationRunner(fail_if_called)
        result = runner.verify_after_execution(
            self.execution_result(result={"verificationRequired": False}),
            expected_state="irrelevant",
            request_id="req-1",
        )

        self.assertEqual(result["status"], "skipped")
        self.assertFalse(result["observationRan"])
        self.assertTrue(result["continueAutomation"])

    def test_detects_failed_permission_prompt_error_dialog_unchanged_and_lost_focus(self):
        cases = [
            (self.observation(matchedExpectedState=False, targetStillUnchanged=True), "failed", "target_unchanged"),
            (self.observation(permissionPromptVisible=True), "blockedByPrompt", "permission_prompt"),
            (self.observation(errorDialogVisible=True), "failed", "error_dialog"),
            (self.observation(appLostFocus=True), "failed", "app_lost_focus"),
            (self.observation(matchedExpectedState=False, screenChanged=False), "failed", "screen_unchanged"),
        ]

        for observation, status, reason in cases:
            with self.subTest(reason=reason):
                result = self.verify_post_action_observation(
                    self.execution_result(),
                    observation,
                    expected_state="settings dialog opened",
                    request_id="req-1",
                )
                self.assertEqual(result["status"], status)
                self.assertEqual(result["reason"], reason)
                self.assertFalse(result["continueAutomation"])

    def test_uncertain_result_stops_scoped_automation_and_requests_guidance(self):
        result = self.verify_post_action_observation(
            self.execution_result(),
            self.observation(matchedExpectedState=False, screenChanged=True, summary="changed but unclear"),
            expected_state="settings dialog opened",
            request_id="req-1",
        )

        self.assertEqual(result["status"], "uncertain")
        self.assertFalse(result["continueAutomation"])
        self.assertTrue(result["requiresUserGuidance"])


if __name__ == "__main__":
    unittest.main()

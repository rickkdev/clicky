import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent


def load_smoke_module():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin_smoke_tests.smoke_fixtures",
        ROOT / "smoke_fixtures.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class DesktopTaskSmokeFixtureTests(unittest.TestCase):
    def setUp(self):
        self.smoke = load_smoke_module()

    def test_windows_fixture_is_opt_in_safe_and_covers_open_click_type_verify(self):
        fixture = self.smoke.get_fixture("windows")

        self.assertEqual(fixture["platform"], "windows")
        self.assertTrue(fixture["optInOnly"])
        self.assertEqual(fixture["optInEnvironmentVariable"], "CLICKY_RUN_DESKTOP_SMOKE")
        self.assertEqual(fixture["defaultMode"], "fake")
        self.assertIn("notepad", fixture["harmlessApplication"].lower())
        self.assertEqual([step["actionType"] for step in fixture["steps"]], ["openApplication", "focusWindow", "click", "typeText", "waitForScreenChange"])
        self.assertIn("expectedText", fixture["verification"])
        self.assertEqual(fixture["prohibitedActions"], ["destructiveActions", "networkPurchases", "realAccountMessages", "credentialEntry"])

    def test_macos_fixture_is_opt_in_safe_and_covers_open_click_type_verify(self):
        fixture = self.smoke.get_fixture("macos")

        self.assertEqual(fixture["platform"], "macos")
        self.assertTrue(fixture["optInOnly"])
        self.assertEqual(fixture["defaultMode"], "fake")
        self.assertIn("textedit", fixture["harmlessApplication"].lower())
        self.assertEqual([step["actionType"] for step in fixture["steps"]], ["openApplication", "focusWindow", "click", "typeText", "waitForScreenChange"])
        self.assertIn("expectedText", fixture["verification"])

    def test_default_ci_runner_uses_fake_provider_and_never_executes_desktop_actions(self):
        windows = self.smoke.run_fixture("windows", env={})
        macos = self.smoke.run_fixture("macos", env={})

        for result in [windows, macos]:
            self.assertEqual(result["status"], "passed")
            self.assertEqual(result["mode"], "fake")
            self.assertFalse(result["desktopActionsExecuted"])
            self.assertEqual(result["stepsPassed"], 5)
            self.assertEqual(result["verification"]["status"], "success")

    def test_macos_youtube_action_smoke_builds_safe_clicky_execute_steps(self):
        steps = self.smoke.build_macos_youtube_action_smoke_steps("https://www.youtube.com/watch?v=pAgnJDJN4VA")

        self.assertEqual([step["method"] for step in steps], ["clicky.executeAction", "clicky.executeAction", "clicky.executeAction", "clicky.executeAction"])
        proposals = [step["params"]["proposal"] for step in steps]
        self.assertEqual([proposal["actionType"] for proposal in proposals], ["openApplication", "hotkey", "typeText", "hotkey"])
        self.assertEqual(proposals[0]["application"], "Google Chrome")
        self.assertEqual(proposals[1]["hotkey"], ["cmd", "l"])
        self.assertEqual(proposals[2]["inputPreview"], "https://www.youtube.com/watch?v=pAgnJDJN4VA")
        self.assertEqual(proposals[3]["hotkey"], ["enter"])
        for step in steps:
            params = step["params"]
            proposal = params["proposal"]
            self.assertFalse(proposal["requiresConfirmation"])
            self.assertEqual(params["permissionDecision"]["decision"], "allow")
            self.assertEqual(params["safetyDecision"]["proposalId"], proposal["id"])
            self.assertTrue(params["safetyDecision"]["forwardToExecutor"])

    def test_macos_youtube_action_smoke_rejects_non_youtube_url(self):
        with self.assertRaises(ValueError):
            self.smoke.build_macos_youtube_action_smoke_steps("https://example.com")

    def test_run_desktop_smoke_emits_macos_youtube_plan_without_executing_actions(self):
        completed = subprocess.run(
            [sys.executable, str(ROOT / "scripts" / "run_desktop_smoke.py"), "--platform", "macos", "--macos-youtube-url", "https://www.youtube.com/watch?v=pAgnJDJN4VA"],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=5,
            check=False,
        )

        self.assertEqual(completed.returncode, 0, completed.stderr)
        result = json.loads(completed.stdout)
        self.assertEqual(result["status"], "planned")
        self.assertFalse(result["desktopActionsExecuted"])
        self.assertEqual(result["steps"][3]["params"]["proposal"]["hotkey"], ["enter"])

    def test_smoke_docs_document_opt_in_commands_and_log_locations(self):
        docs = [
            REPO / "windows" / "docs" / "hermes-bridge-e2e-desktop-smoke.md",
            REPO / "mac" / "docs" / "hermes-bridge-e2e-desktop-smoke.md",
        ]
        for doc in docs:
            with self.subTest(doc=doc):
                text = doc.read_text(encoding="utf-8")
                self.assertIn("CLICKY_RUN_DESKTOP_SMOKE=1", text)
                self.assertIn("default CI", text)
                self.assertIn("fake", text)
                self.assertIn("logs", text.lower())
                self.assertIn("no purchases", text.lower())
                self.assertIn("no credentials", text.lower())


if __name__ == "__main__":
    unittest.main()

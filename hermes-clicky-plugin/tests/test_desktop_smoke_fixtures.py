import importlib.util
import json
import subprocess
import sys
import tempfile
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

        self.assertEqual([step["method"] for step in steps], ["clicky.executeAction"])
        proposals = [step["params"]["proposal"] for step in steps]
        self.assertEqual([proposal["actionType"] for proposal in proposals], ["openUrl"])
        self.assertEqual(proposals[0]["url"], "https://www.youtube.com/watch?v=pAgnJDJN4VA")
        self.assertEqual(proposals[0]["browser"], "Google Chrome")
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

    def test_macos_youtube_action_smoke_executes_only_when_opted_in_with_bridge(self):
        calls = []

        def fake_bridge_call(method, params):
            calls.append({"method": method, "params": params})
            return {"protocolVersion": "clicky.hermes.v1", "ok": True, "status": "executed", "proposalId": params["proposal"]["id"], "actionType": params["proposal"]["actionType"]}

        result = self.smoke.run_macos_youtube_action_smoke(
            "https://www.youtube.com/watch?v=pAgnJDJN4VA",
            env={"CLICKY_RUN_DESKTOP_SMOKE": "1"},
            bridge_call=fake_bridge_call,
        )

        self.assertEqual(result["status"], "passed")
        self.assertTrue(result["desktopActionsExecuted"])
        self.assertEqual(result["stepsExecuted"], 1)
        self.assertEqual([call["params"]["proposal"]["actionType"] for call in calls], ["openUrl"])

    def test_macos_youtube_action_smoke_blocks_without_opt_in(self):
        result = self.smoke.run_macos_youtube_action_smoke(
            "https://www.youtube.com/watch?v=pAgnJDJN4VA",
            env={},
            bridge_call=lambda method, params: self.fail("bridge must not be called without opt-in"),
        )

        self.assertEqual(result["status"], "blocked")
        self.assertFalse(result["desktopActionsExecuted"])

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
        self.assertEqual(result["steps"][0]["params"]["proposal"]["actionType"], "openUrl")

    def test_run_desktop_smoke_real_macos_youtube_uses_fake_bridge_command_when_opted_in(self):
        with tempfile.NamedTemporaryFile("w", suffix=".py", delete=False) as handle:
            handle.write(
                "#!/usr/bin/env python3\n"
                "import json, sys\n"
                "req=json.loads(sys.stdin.readline())\n"
                "proposal=req['params']['proposal']\n"
                "print(json.dumps({'jsonrpc':'2.0','id':req['id'],'result':{'protocolVersion':'clicky.hermes.v1','ok':True,'status':'executed','proposalId':proposal['id'],'actionType':proposal['actionType']}}), flush=True)\n"
            )
            bridge = handle.name
        Path(bridge).chmod(0o755)
        completed = subprocess.run(
            [sys.executable, str(ROOT / "scripts" / "run_desktop_smoke.py"), "--platform", "macos", "--real", "--macos-youtube-url", "https://www.youtube.com/watch?v=pAgnJDJN4VA"],
            env={"CLICKY_RUN_DESKTOP_SMOKE": "1", "CLICKY_BRIDGE_COMMAND": f"{sys.executable} {bridge}"},
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=5,
            check=False,
        )

        self.assertEqual(completed.returncode, 0, completed.stderr)
        result = json.loads(completed.stdout)
        self.assertEqual(result["status"], "passed")
        self.assertTrue(result["desktopActionsExecuted"])
        self.assertEqual(result["stepsExecuted"], 1)

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

import json
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEMO = ROOT / "examples" / "demo_workflow.py"
FAKE_BRIDGE = ROOT / "scripts" / "fake_bridge.py"
DOC = ROOT / "examples" / "demo_workflow.md"


class DemoWorkflowTests(unittest.TestCase):
    def test_demo_workflow_script_runs_full_point_then_explain_flow_with_timing(self):
        completed = subprocess.run(
            [
                sys.executable,
                str(DEMO),
                "--bridge-command",
                f"{sys.executable} {FAKE_BRIDGE}",
                "--target",
                "settings",
                "--task",
                "show me where to configure the repository",
                "--json",
            ],
            cwd=ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=5,
            check=False,
        )

        self.assertEqual(completed.returncode, 0, completed.stderr)
        result = json.loads(completed.stdout)

        self.assertEqual(result["protocolVersion"], "clicky.hermes.v1")
        self.assertTrue(result["ok"])
        self.assertEqual(result["status"], "demo_completed")
        self.assertEqual(result["platform"], "windows")
        self.assertEqual(result["userRequest"], "show me where to configure the repository")
        self.assertEqual(result["target"], "settings")
        self.assertEqual(
            [step["name"] for step in result["steps"]],
            ["bridgeHealth", "observeScreen", "pointToTarget", "explainScreen", "hermesResponse"],
        )
        self.assertTrue(result["pointing"]["overlayRendered"])
        self.assertEqual(result["pointing"]["status"], "pointed")
        self.assertIn("fake bridge", result["explanation"])
        for key in ["bridgeStartupMs", "screenCaptureMs", "modelResponseMs", "overlayRenderMs", "totalMs"]:
            self.assertIn(key, result["timingMs"])
            self.assertIsInstance(result["timingMs"][key], (int, float))
        self.assertGreaterEqual(len(result["failureCases"]), 3)
        self.assertNotIn("osControl", json.dumps(result["steps"]))

    def test_demo_workflow_docs_capture_run_command_timing_and_failure_cases(self):
        text = DOC.read_text(encoding="utf-8")

        self.assertIn("demo_workflow.py", text)
        self.assertIn("pointToTarget", text)
        self.assertIn("explainScreen", text)
        self.assertIn("bridgeStartupMs", text)
        self.assertIn("screenCaptureMs", text)
        self.assertIn("modelResponseMs", text)
        self.assertIn("overlayRenderMs", text)
        self.assertGreaterEqual(text.count("Failure case"), 3)
        self.assertIn("Windows", text)


if __name__ == "__main__":
    unittest.main()

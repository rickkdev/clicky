import importlib.util
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load_plugin_package():
    spec = importlib.util.spec_from_file_location(
        "hermes_clicky_plugin",
        ROOT / "__init__.py",
        submodule_search_locations=[str(ROOT)],
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class MacOSCapabilityAdapterTests(unittest.TestCase):
    def test_reports_permissions_independently_when_granted(self):
        plugin = load_plugin_package()
        probe = plugin.macos_capabilities.MacOSPermissionState(
            screen_recording=True,
            accessibility=True,
        )

        result = plugin.macos_capabilities.build_macos_capabilities(probe)

        self.assertEqual(result["protocolVersion"], "clicky.hermes.v1")
        self.assertTrue(result["ok"])
        self.assertEqual(result["status"], "ready")
        self.assertEqual(result["platform"], "macos")
        self.assertTrue(result["permissions"]["screenRecording"]["granted"])
        self.assertTrue(result["permissions"]["accessibility"]["granted"])
        self.assertFalse(result["capabilities"]["observeScreen"]["enabled"])
        self.assertEqual(result["capabilities"]["observeScreen"]["reason"], "macos_observe_not_wired")
        self.assertFalse(result["capabilities"]["explainScreen"]["enabled"])
        self.assertEqual(result["capabilities"]["explainScreen"]["reason"], "macos_explain_not_wired")
        self.assertFalse(result["capabilities"]["pointToTarget"]["enabled"])
        self.assertEqual(result["capabilities"]["pointToTarget"]["reason"], "macos_point_not_wired")
        self.assertFalse(result["capabilities"]["overlay"]["enabled"])
        self.assertEqual(result["capabilities"]["overlay"]["reason"], "macos_overlay_not_wired")
        self.assertFalse(result["capabilities"]["osControl"]["enabled"])
        self.assertEqual(result["capabilities"]["osControl"]["reason"], "phase_2_not_implemented")

    def test_missing_screen_recording_disables_screen_dependent_capabilities_explicitly(self):
        plugin = load_plugin_package()
        probe = plugin.macos_capabilities.MacOSPermissionState(
            screen_recording=False,
            accessibility=True,
        )

        result = plugin.macos_capabilities.build_macos_capabilities(probe)

        self.assertFalse(result["permissions"]["screenRecording"]["granted"])
        self.assertEqual(result["permissions"]["screenRecording"]["reason"], "missing_screen_recording_permission")
        self.assertEqual(result["capabilities"]["observeScreen"]["reason"], "missing_screen_recording_permission")
        self.assertEqual(result["capabilities"]["explainScreen"]["reason"], "missing_screen_recording_permission")
        self.assertEqual(result["capabilities"]["pointToTarget"]["reason"], "missing_screen_recording_permission")
        self.assertEqual(result["capabilities"]["overlay"]["reason"], "macos_overlay_not_wired")
        self.assertEqual(result["capabilities"]["osControl"]["reason"], "phase_2_not_implemented")

    def test_missing_accessibility_disables_overlay_and_future_control_without_claiming_os_control(self):
        plugin = load_plugin_package()
        probe = plugin.macos_capabilities.MacOSPermissionState(
            screen_recording=True,
            accessibility=False,
        )

        result = plugin.macos_capabilities.build_macos_capabilities(probe)

        self.assertFalse(result["permissions"]["accessibility"]["granted"])
        self.assertEqual(result["permissions"]["accessibility"]["reason"], "missing_accessibility_permission")
        self.assertFalse(result["capabilities"]["overlay"]["enabled"])
        self.assertEqual(result["capabilities"]["overlay"]["reason"], "missing_accessibility_permission")
        self.assertFalse(result["capabilities"]["osControl"]["enabled"])
        self.assertEqual(result["capabilities"]["osControl"]["reason"], "phase_2_not_implemented")

    def test_readme_documents_macos_permission_setup(self):
        readme = (ROOT / "README.md").read_text(encoding="utf-8")

        self.assertIn("macOS permissions", readme)
        self.assertIn("Screen Recording", readme)
        self.assertIn("Accessibility", readme)
        self.assertIn("phase 2", readme)

    def test_get_capabilities_uses_macos_adapter_without_bridge_when_bridge_is_unavailable(self):
        plugin = load_plugin_package()
        plugin.tools.platform_module.system = lambda: "Darwin"
        plugin.tools._bridge_or_error = lambda method, params: {
            "protocolVersion": "clicky.hermes.v1",
            "ok": False,
            "status": "bridge_unavailable",
            "error": {"code": "bridge_unavailable", "message": "not configured", "retryable": False},
        }
        plugin.macos_capabilities.probe_macos_permissions = lambda: plugin.macos_capabilities.MacOSPermissionState(
            screen_recording=False,
            accessibility=False,
        )

        result = json.loads(plugin.tools.get_clicky_capabilities({"platform": "auto"}))

        self.assertEqual(result["platform"], "macos")
        self.assertEqual(result["status"], "ready")
        self.assertEqual(result["capabilities"]["observeScreen"]["reason"], "missing_screen_recording_permission")
        self.assertEqual(result["capabilities"]["overlay"]["reason"], "missing_accessibility_permission")


if __name__ == "__main__":
    unittest.main()

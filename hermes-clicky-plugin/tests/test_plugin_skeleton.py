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


class FakeHermesContext:
    def __init__(self):
        self.registered = []

    def register_tool(self, **kwargs):
        self.registered.append(kwargs)


class PluginSkeletonTests(unittest.TestCase):
    def test_required_self_contained_plugin_files_exist(self):
        for relative_path in [
            "plugin.yaml",
            "README.md",
            "__init__.py",
            "schemas.py",
            "tools.py",
            "bridge_client.py",
            "protocol/schema.json",
            "protocol/examples/capabilities.windows.json",
            "protocol/examples/observe.metadata-only.json",
            "protocol/examples/explain.success.json",
            "protocol/examples/point.success.json",
            "protocol/examples/point.low-confidence.json",
            "scripts/validate_protocol.py",
            "examples/demo_point.py",
            "examples/demo_explain.py",
        ]:
            self.assertTrue((ROOT / relative_path).exists(), f"missing {relative_path}")

    def test_plugin_yaml_declares_tools_and_platform_support(self):
        text = (ROOT / "plugin.yaml").read_text(encoding="utf-8")
        for tool_name in [
            "get_clicky_capabilities",
            "observe_clicky_screen",
            "explain_clicky_screen",
            "point_clicky_target",
        ]:
            self.assertIn(tool_name, text)

        self.assertIn("platforms:", text)
        self.assertIn("windows", text)
        self.assertIn("macos", text)
        self.assertIn("unsupported", text)

    def test_register_adds_four_clicky_tools_with_schemas_and_handlers(self):
        plugin = load_plugin_package()
        ctx = FakeHermesContext()

        plugin.register(ctx)

        names = [tool["name"] for tool in ctx.registered]
        self.assertEqual(
            names,
            [
                "get_clicky_capabilities",
                "observe_clicky_screen",
                "explain_clicky_screen",
                "point_clicky_target",
            ],
        )
        for tool in ctx.registered:
            self.assertEqual(tool["toolset"], "clicky")
            self.assertIsInstance(tool["schema"], dict)
            self.assertTrue(callable(tool["handler"]))

    def test_get_capabilities_reports_unsupported_platform_without_bridge_launch(self):
        plugin = load_plugin_package()
        plugin.tools.platform_module.system = lambda: "Linux"

        result = json.loads(plugin.tools.get_clicky_capabilities({"platform": "auto"}))

        self.assertEqual(result["protocolVersion"], "clicky.hermes.v1")
        self.assertFalse(result["ok"])
        self.assertEqual(result["status"], "unsupported_platform")
        self.assertEqual(result["platform"], "linux")
        self.assertIn("windows", result["supportedPlatforms"])
        self.assertIn("macos", result["supportedPlatforms"])
        self.assertEqual(result["error"]["code"], "unsupported_platform")

    def test_explain_tool_passes_optional_observation_references_to_bridge(self):
        plugin = load_plugin_package()
        captured = {}

        def fake_bridge(method, params):
            captured["method"] = method
            captured["params"] = params
            return {"protocolVersion": "clicky.hermes.v1", "ok": True, "status": "explained", "explanation": "done"}

        plugin.tools._bridge_or_error = fake_bridge

        result = json.loads(plugin.tools.explain_clicky_screen({
            "task": "explain the dialog",
            "observationId": "obs-123",
            "screenId": "display-1",
        }))

        self.assertTrue(result["ok"])
        self.assertEqual(captured["method"], "clicky.explainScreen")
        self.assertEqual(captured["params"]["task"], "explain the dialog")
        self.assertEqual(captured["params"]["observationId"], "obs-123")
        self.assertEqual(captured["params"]["screenId"], "display-1")

    def test_tool_handlers_do_not_make_ad_hoc_subprocess_calls(self):
        tools_text = (ROOT / "tools.py").read_text(encoding="utf-8")
        self.assertNotIn("subprocess", tools_text)
        self.assertIn("ClickyBridgeClient", tools_text)


if __name__ == "__main__":
    unittest.main()

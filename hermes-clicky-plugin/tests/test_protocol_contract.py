import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = json.loads((ROOT / "protocol" / "schema.json").read_text(encoding="utf-8"))
DEFS = SCHEMA["$defs"]


class ProtocolContractTests(unittest.TestCase):
    def test_protocol_document_exists_and_defines_all_methods(self):
        doc_path = ROOT / "protocol" / "README.md"
        self.assertTrue(doc_path.exists(), "protocol/README.md must document the shared contract")
        text = doc_path.read_text(encoding="utf-8")
        for method in ["getCapabilities", "observeScreen", "explainScreen", "pointToTarget"]:
            self.assertIn(method, text)
        self.assertIn("protocolVersion", text)
        self.assertIn("JSON-RPC over stdio", text)

    def test_stdio_transport_documents_why_network_transports_are_deferred(self):
        text = (ROOT / "protocol" / "README.md").read_text(encoding="utf-8")
        self.assertIn("localhost", text)
        self.assertIn("WebSocket", text)
        self.assertIn("deferred", text)

    def test_schema_defines_requests_and_responses_for_all_methods(self):
        for name in [
            "getCapabilitiesRequest",
            "getCapabilitiesResponse",
            "observeScreenRequest",
            "observeScreenResponse",
            "explainScreenRequest",
            "explainScreenResponse",
            "pointToTargetRequest",
            "pointToTargetResponse",
        ]:
            self.assertIn(name, DEFS, f"missing schema definition: {name}")

    def test_all_success_responses_are_versioned(self):
        for name in ["capabilitiesResponse", "observeScreenResponse", "explainScreenResponse", "pointToTargetResponse"]:
            required = set(DEFS[name].get("required", []))
            self.assertIn("protocolVersion", required, f"{name} must require protocolVersion")

    def test_examples_cover_required_response_cases_and_are_versioned(self):
        examples = {p.name: json.loads(p.read_text(encoding="utf-8")) for p in (ROOT / "protocol" / "examples").glob("*.json")}
        for required in [
            "capabilities.windows.json",
            "observe.metadata-only.json",
            "explain.success.json",
            "point.success.json",
            "point.low-confidence.json",
        ]:
            self.assertIn(required, examples)
        for name, data in examples.items():
            self.assertEqual(data.get("protocolVersion"), "clicky.hermes.v1", f"{name} missing protocolVersion")

    def test_action_proposal_protocol_defines_all_action_types_and_safety_fields(self):
        self.assertIn("actionProposal", DEFS)
        proposal = DEFS["actionProposal"]
        action_type = proposal["properties"]["actionType"]
        self.assertEqual(
            set(action_type["enum"]),
            {
                "click",
                "doubleClick",
                "typeText",
                "hotkey",
                "openApplication",
                "focusWindow",
                "waitForScreenChange",
                "stop",
            },
        )
        for field in ["targetLabel", "confidence", "riskLevel", "requiresConfirmation", "rationale"]:
            self.assertIn(field, proposal["required"], f"actionProposal must require {field}")
        self.assertIn("coordinates", proposal["properties"])
        self.assertIn("nativeSelector", proposal["properties"])

    def test_action_proposal_response_is_separate_from_visual_pointing_and_defaults_to_confirmation(self):
        self.assertIn("actionProposalsResponse", DEFS)
        response = DEFS["actionProposalsResponse"]
        self.assertIn("actionMode", response["required"])
        self.assertEqual(response["properties"]["actionMode"].get("default"), "confirmBeforeAction")
        self.assertIn("confirmBeforeAction", response["properties"]["actionMode"]["enum"])
        self.assertIn("proposals", response["required"])
        self.assertNotIn("overlayRendered", response["properties"], "action proposals must not masquerade as visual pointing results")
        self.assertNotIn("actionMode", DEFS["pointToTargetResponse"]["properties"], "pointing results must stay visual, not executable")

    def test_action_proposal_examples_cover_low_risk_high_risk_and_blocked_cases(self):
        examples = {p.name: json.loads(p.read_text(encoding="utf-8")) for p in (ROOT / "protocol" / "examples").glob("*.json")}
        for required in [
            "action.low-risk-click.json",
            "action.high-risk-destructive.json",
            "action.blocked.json",
        ]:
            self.assertIn(required, examples)

        low = examples["action.low-risk-click.json"]
        self.assertEqual(low["actionMode"], "confirmBeforeAction")
        self.assertEqual(low["proposals"][0]["actionType"], "click")
        self.assertEqual(low["proposals"][0]["riskLevel"], "low")

        high = examples["action.high-risk-destructive.json"]
        self.assertEqual(high["proposals"][0]["riskLevel"], "high")
        self.assertTrue(high["proposals"][0]["requiresConfirmation"])

        blocked = examples["action.blocked.json"]
        self.assertEqual(blocked["status"], "blocked")
        self.assertEqual(blocked["proposals"][0]["riskLevel"], "blocked")
        self.assertTrue(blocked["proposals"][0]["requiresConfirmation"])

    def test_protocol_does_not_leak_native_or_provider_internals(self):
        doc_path = ROOT / "protocol" / "README.md"
        doc_text = doc_path.read_text(encoding="utf-8") if doc_path.exists() else ""
        combined = json.dumps(SCHEMA) + "\n" + doc_text
        forbidden = ["WPF", "Win32", "ScreenCaptureKit", "Swift", "Codex", "Claude", "Anthropic", "OpenAI", "AssemblyAI", "ElevenLabs"]
        for term in forbidden:
            self.assertNotIn(term, combined, f"protocol leaks native/provider term: {term}")


if __name__ == "__main__":
    unittest.main()

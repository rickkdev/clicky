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

    def test_protocol_does_not_leak_native_or_provider_internals(self):
        doc_path = ROOT / "protocol" / "README.md"
        doc_text = doc_path.read_text(encoding="utf-8") if doc_path.exists() else ""
        combined = json.dumps(SCHEMA) + "\n" + doc_text
        forbidden = ["WPF", "Win32", "ScreenCaptureKit", "Swift", "Codex", "Claude", "Anthropic", "OpenAI", "AssemblyAI", "ElevenLabs"]
        for term in forbidden:
            self.assertNotIn(term, combined, f"protocol leaks native/provider term: {term}")


if __name__ == "__main__":
    unittest.main()

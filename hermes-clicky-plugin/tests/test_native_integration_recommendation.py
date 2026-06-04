import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOC = ROOT / "docs" / "native-hermes-integration-recommendation.md"


class NativeIntegrationRecommendationDocTests(unittest.TestCase):
    def test_recommendation_doc_covers_us010_required_decision_evidence(self):
        text = DOC.read_text(encoding="utf-8")
        lowered = text.lower()

        for required in [
            "architecture",
            "reliability",
            "latency",
            "permission friction",
            "product value",
            "privacy",
            "permissions",
            "model cost",
            "support burden",
            "native integration roadmap",
        ]:
            self.assertIn(required, lowered)

        for method in ["getCapabilities", "observeScreen", "explainScreen", "pointToTarget"]:
            self.assertIn(method, text)

        self.assertIn("JSON-RPC stdio", text)
        self.assertIn("no os control", lowered)
        self.assertIn("no tray auto-launch", lowered)

    def test_recommendation_doc_includes_measured_timing_and_windows_log_evidence(self):
        text = DOC.read_text(encoding="utf-8")

        for timing_key in [
            "bridgeStartupMs",
            "screenCaptureMs",
            "modelResponseMs",
            "overlayRenderMs",
            "totalMs",
        ]:
            self.assertIn(timing_key, text)

        self.assertRegex(text, r"bridgeStartupMs\s*[:=]\s*\d+\.\d+")
        self.assertRegex(text, r"screenCaptureMs\s*[:=]\s*\d+\.\d+")
        self.assertRegex(text, r"modelResponseMs\s*[:=]\s*\d+\.\d+")
        self.assertIn("[TURN]", text)
        self.assertIn("diagnostic-text-llm", text)
        self.assertIn("diagnostic-image-llm", text)
        self.assertIn("capture-", text)
        self.assertIn("diagnostic-tts", text)
        self.assertRegex(text.lower(), r"windows turn timing logs[^\n]+available")

    def test_recommendation_doc_includes_semantic_pointing_fixture_accuracy_evidence(self):
        text = DOC.read_text(encoding="utf-8")
        lowered = text.lower()

        for fixture_id in [
            "world-map-egypt",
            "world-map-algeria",
            "nearby-country-distractors",
            "dense-settings-panel",
            "simple-labeled-ui-target",
        ]:
            self.assertIn(fixture_id, text)

        self.assertIn("5 fixtures", lowered)
        self.assertIn("11 canned outputs", lowered)
        self.assertIn("allowed region", lowered)
        self.assertIn("distractor", lowered)
        self.assertIn("[POINT:none]", text)
        self.assertIn("SemanticPointingEvaluationTests", text)

    def test_recommendation_doc_separates_native_runtime_from_hermes_scope(self):
        text = DOC.read_text(encoding="utf-8")
        lowered = text.lower()

        native_section = re.search(r"what stays native runtime(?P<body>.*?)(\n## |\Z)", text, re.IGNORECASE | re.DOTALL)
        hermes_section = re.search(r"what moves into hermes(?P<body>.*?)(\n## |\Z)", text, re.IGNORECASE | re.DOTALL)
        self.assertIsNotNone(native_section)
        self.assertIsNotNone(hermes_section)

        native_body = native_section.group("body").lower()
        hermes_body = hermes_section.group("body").lower()

        for native_term in ["screen capture", "overlay", "permissions", "coordinate", "native bridge"]:
            self.assertIn(native_term, native_body)
        for hermes_term in ["plugin", "tool", "policy", "audit", "roadmap"]:
            self.assertIn(hermes_term, hermes_body)

        self.assertNotIn("click/type", lowered.split("native integration roadmap", 1)[0])


if __name__ == "__main__":
    unittest.main()

# Native Hermes integration recommendation

Recommendation: keep Clicky as a native runtime behind the Hermes Clicky plugin for V1. Do not fold platform capture, overlay, and permission handling directly into Hermes yet. The plugin evidence is strong enough to make Clicky a first-class Hermes desktop-vision capability, but not strong enough to justify a native Hermes rewrite.

## Architecture

Current shape is the right boundary:

- Hermes-facing package: `hermes-clicky-plugin/`
- transport: JSON-RPC stdio only
- plugin tools: `getCapabilities`, `observeScreen`, `explainScreen`, `pointToTarget`
- native backends: `windows/src/Clicky.Bridge/` and `mac/hermes-clicky-bridge/clicky_macos_bridge.py`
- V1 behavior: observe, explain, point, optional overlay
- no OS control
- no tray auto-launch

This is clean because Hermes sees semantic tool results, not WPF, Win32, macOS Accessibility, screenshot internals, ScreenCaptureKit, provider payloads, or tray state.

## Reliability

Reliable enough for a V1 integration demo:

- deterministic plugin tests cover bridge unavailable, timeout, invalid JSON, structured errors, protocol examples, macOS capability fallback, macOS bridge stdio, and demo workflow
- Windows bridge tests use fake capture/provider/overlay seams instead of compositor-dependent CI
- macOS bridge tests use deterministic environment seams and keep real TCC behavior in manual smoke docs
- low-confidence and no-target pointing return non-actionable statuses instead of fabricating a point

Not yet reliable enough for native desktop automation. That would need action proposals, permission tiers, policy, confirmation, audit logs, and post-action verification first.

## Latency evidence

Measured demo workflow with fake bridge on this branch:

- bridgeStartupMs: 30.713
- screenCaptureMs: 29.953
- modelResponseMs: 53.097
- overlayRenderMs: 27.162
- totalMs: 113.813

Command used:

```bash
python3 hermes-clicky-plugin/examples/demo_workflow.py \
  --bridge-command "python3 hermes-clicky-plugin/scripts/fake_bridge.py" \
  --platform windows \
  --target "settings" \
  --task "show me where to configure the repository" \
  --json
```

Interpretation: plugin overhead is cheap. Real latency will be dominated by screen capture and model calls, not JSON-RPC stdio.

Windows turn timing logs are available through `[TURN]` instrumentation in `%APPDATA%\\Clicky\\debug.log`; no local Windows provider run was available in this macOS session. The available timing evidence is the stable log format plus diagnostics plumbing:

- `[TURN] id=... t=...ms provider=... model=... event=...`
- `diagnostic-text-llm-start` / `diagnostic-text-llm-end`
- `capture-start` / `capture-*` screen evidence with image bytes and cursor-screen filtering
- `diagnostic-image-llm-start` / `diagnostic-image-llm-end`
- `diagnostic-tts-start` / `diagnostic-tts-*`

Run the tray `Provider Timing Diagnostics` item on Windows for real provider timings before blaming overlay or vision quality.

## Permission friction

Permission friction is real but acceptable for observe/point:

- Windows capture permission behavior is already represented as structured `permission_denied` errors
- macOS requires Screen Recording for capture and Accessibility for overlay/point rendering
- macOS TCC changes often require host process restart
- default `metadataOnly` avoids screenshot file writes unless requested

The support docs must be blunt: if Screen Recording or Accessibility is missing, Hermes should ask the user to grant permission and retry. Silent failure would be worse.

## Product value

The V1 product path is obvious and useful:

1. user asks where to click
2. Hermes calls `observeScreen`
3. Hermes calls `pointToTarget`
4. Clicky renders a visible pointer/annotation
5. Hermes calls `explainScreen` and answers in words

This is valuable without taking control of the OS. The pointer turns a vague assistant response into visible guidance. That is the right first product: guidance, not automation.

## Pointing accuracy evidence

Semantic pointing evidence lives in `windows/tests/Clicky.Tests/Fixtures/pointing/` and is enforced by `SemanticPointingEvaluationTests`.

Current fixture gate:

- 5 fixtures
- 11 canned outputs
- fixtures: `world-map-egypt`, `world-map-algeria`, `nearby-country-distractors`, `dense-settings-panel`, `simple-labeled-ui-target`
- every fixture has screenshot-style PNG evidence plus allowed region polygons and distractor polygons
- a point passes only if it lands inside an allowed region and outside every distractor
- `[POINT:none]` is accepted for ambiguous map cases where a wrong pointer would be worse than no pointer
- required UI targets reject `[POINT:none]`

This is the right accuracy standard. It tests the product failure users actually feel: a confident pointer in the wrong country, water, or neighboring field.

## What stays native runtime

Keep these in Windows/macOS native runtime code:

- screen capture and display enumeration
- overlay rendering and pointer animation
- platform permissions and TCC/Win32 permission probes
- coordinate conversion between screenshot pixels, normalized coordinates, display bounds, DPI scale, and physical pixels
- native bridge hosts and native bridge diagnostics
- provider-specific model host plumbing until Hermes owns a mature vision-routing path
- desktop compositor smoke tests and local provider timing runs

Reason: these are platform-specific, permission-sensitive, and hard to fake safely inside Hermes core.

## What moves into Hermes

Move or keep these in Hermes/plugin land:

- plugin manifest and tool registration
- semantic protocol schemas and examples
- JSON-RPC bridge client
- user-facing tool descriptions and error copy
- capability negotiation
- screenshot policy defaults (`metadataOnly` first)
- policy, confirmation, and audit design for future OS-control stories
- roadmap docs and validation scripts

Hermes should own orchestration and user trust semantics. Native runtimes should own pixels, permissions, overlays, and platform quirks.

## Risks

Privacy:

- screenshots can expose credentials, private messages, documents, and account data
- default must remain metadata-only unless image bytes or files are explicitly requested
- logs must avoid raw screenshot paths/content unless debug mode is explicit

Permissions:

- macOS Screen Recording and Accessibility are high-friction and user-visible
- Windows capture failures need clear retryable permission errors
- support docs must explain restarts after permission grants

Model cost:

- real image LLM calls dominate cost and latency
- point/explain should avoid duplicate capture and duplicate vision calls where possible
- future Hermes-owned inference needs provider routing and budget controls

Support burden:

- monitor scaling, negative-origin displays, multi-monitor cursor filtering, and compositor overlays are endless edge cases
- semantic pointing quality varies by provider and target type
- Windows and macOS diagnostics must stay separate because failure modes differ

## Native integration roadmap

1. Keep V1 as a Hermes plugin plus native bridge. Package `hermes-clicky-plugin/` as the installable Hermes surface.
2. Add real Windows timing evidence from `Provider Timing Diagnostics`: text LLM, image LLM, capture, TTS, point parse, overlay dispatch.
3. Add real macOS smoke evidence for Screen Recording, Accessibility, metadata capture, file capture, point, and overlay.
4. Promote the protocol to a Hermes desktop-vision contract only after observe/explain/point are boring on both platforms.
5. Add lower-level native primitives: observe current screen, render pointer, render circle, render arrow, clear overlay.
6. Let Hermes own more inference only after it can route vision models, enforce cost limits, and preserve the semantic fixture gate.
7. Start Phase 2 with action proposals, not execution: proposed action schema, permission tiers, conservative safety policy, confirmation UX, audit log, and post-action verification.
8. Only after that, add controlled desktop action execution for approved low-risk tasks.

Bottom line: native Hermes integration should proceed, but as a staged desktop-vision integration. A rewrite into Hermes core now would be premature and would bury the hard platform work in the wrong place.

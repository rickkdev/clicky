# Hermes Clicky protocol

protocolVersion: `clicky.hermes.v1`

transport for the first native bridge: JSON-RPC over stdio.

localhost and WebSocket transports are deferred. stdio is easier to spawn from Hermes, test with deterministic fixtures, log in-process, and shut down without choosing ports, opening firewall prompts, or leaving network listeners behind. network transports can come later if the native runtime needs long-lived streaming or remote control.

this protocol is the only contract Hermes should rely on. it describes semantic screen observation, screen explanation, pointing, and capability reporting. it does not expose native UI framework classes, screenshot implementation details, or model-provider payloads.

canonical schema:

`schema.json`

## design rules

- every request and response includes `protocolVersion`.
- all coordinates returned to Hermes are semantic contract fields, not native runtime objects.
- point responses include normalized coordinates when a target is found.
- point responses include physical coordinates when the native runtime can map them safely.
- low confidence, missing permissions, and missing targets are explicit statuses.
- never fabricate coordinates for uncertain targets.
- screenshots are metadata-only by default.
- image bytes or image file paths are only returned when requested by `imageMode`.
- Windows `metadataOnly` returns display/image/cursor metadata and must not write screenshot files.
- Windows real-capture smoke checks live in `windows/docs/hermes-bridge-observe-smoke.md`; normal CI should use fake capture-provider tests.
- visual pointing results are guidance only; they are not executable action proposals.
- action proposals are inert data structures only. no bridge or plugin code may click, type, press hotkeys, open apps, focus windows, or execute os control from this protocol.
- default action mode / permission tier is `confirmBeforeAction`.
- active permission tier is reported by `getCapabilities`.
- changing permission tier requires an explicit user-facing setting or command; a plugin request cannot silently enable `fullControl`.
- permission-tier evaluation returns policy data (`allow`, `requireConfirmation`, `block`) only. it never executes desktop actions.
- safety-policy evaluation is separate from permission tiers: tiers answer whether the selected trust level allows an action class; safety policy answers whether this specific proposal is safe.
- safety decisions include `decision`, user-readable `reason`, `riskFlags`, and `forwardToExecutor`.
- blocked safety decisions set `forwardToExecutor=false` and produce inert blocked audit records; no executor exists in this phase.
- confirmation UX is Hermes-facing structured state only: no native dialogs, no tray flows, no desktop execution.
- confirmation copy must be concise and must not expose raw rationale, input previews, credentials, or internal reasoning.
- approved confirmations are safety-rechecked immediately before a future executor could receive them; if the recheck blocks, `forwardToExecutor=false`.
- cancelled and stale confirmations produce inert audit events only.
- `executeAction` is exposed to Hermes as `execute_clicky_action` and returns structured execution results from the native bridge.
- execution requests must include the original proposal plus permission, safety, and, when required, confirmation gate results.
- execution results are provider-neutral and must not include raw typed credential text or native UI framework internals.

## methods

### getCapabilities

request:

```json
{
  "protocolVersion": "clicky.hermes.v1",
  "method": "getCapabilities",
  "platform": "auto"
}
```

response shape:

```json
{
  "protocolVersion": "clicky.hermes.v1",
  "ok": true,
  "status": "ready",
  "platform": "windows",
  "capabilities": {
    "observeScreen": { "enabled": true },
    "explainScreen": { "enabled": true },
    "pointToTarget": { "enabled": true },
    "overlay": { "enabled": true },
    "osControl": { "enabled": false, "reason": "phase_2_not_implemented" }
  },
  "activePermissionTier": "confirmBeforeAction",
  "defaultPermissionTier": "confirmBeforeAction",
  "availablePermissionTiers": ["observe", "point", "confirmBeforeAction", "scopedAutopilot", "fullControl"],
  "permissionTierChange": "explicit_user_setting_or_command_required",
  "fullControlPolicy": "external_user_configuration_only"
}
```

### observeScreen

request:

```json
{
  "protocolVersion": "clicky.hermes.v1",
  "method": "observeScreen",
  "imageMode": "metadataOnly",
  "includeCursorScreenOnly": true
}
```

response includes:

- `observationId`
- `platform`
- `capturedAt`
- `coordinateSpace`
- `displays[]`
- `images[]`

### explainScreen

request:

```json
{
  "protocolVersion": "clicky.hermes.v1",
  "method": "explainScreen",
  "task": "explain what controls are visible",
  "observationId": "optional-observeScreen-id",
  "screenId": "optional-display-id"
}
```

response includes:

- `status: explained`
- concise `explanation`
- optional `regions[]` with labels, normalized/physical coordinates, and confidence

Windows bridge notes:

- captures screen context through the existing capture seam unless a future native host supplies an observation cache.
- standalone stdio explanation uses a deterministic `CLICKY_BRIDGE_EXPLANATION_RESPONSE` seam; it does not launch the tray app.
- explanation-only does not render overlays and does not touch speech/TTS.
- provider/model-specific raw payloads stay behind the bridge and are not part of this protocol.

### pointToTarget

request:

```json
{
  "protocolVersion": "clicky.hermes.v1",
  "method": "pointToTarget",
  "target": "repository settings tab",
  "task": "show the user where to configure the repo",
  "renderOverlay": true
}
```

successful response includes:

- `status: pointed`
- `target`
- `label`
- `confidence`
- `normalized`
- `physical` when available
- `reasoning`
- `overlayRendered`

Windows bridge notes:

- point decisions are adapted from the redesigned `PointingTurnResult` / `PointIntent` shape.
- `renderOverlay=false` is a dry run and must not render anything.
- standalone stdio bridge does not launch the tray app for overlays; native runtime hosts can provide an overlay renderer for demos.

non-actionable response statuses include:

- `no_target`
- `low_confidence`
- `permission_denied`
- `bridge_unavailable`
- `error`

### action proposals

`actionProposalsResponse` is a semantic design contract for future desktop control. it is separate from `pointToTargetResponse`: pointing can render a visible pointer, while proposals describe possible future actions as inert data. defining a proposal never executes it.

proposal action types:

- `click`
- `doubleClick`
- `typeText`
- `hotkey`
- `openApplication`
- `focusWindow`
- `waitForScreenChange`
- `stop`

each proposal includes:

- `targetLabel`
- `coordinates` when a safe physical point is available
- `nativeSelector` when a semantic selector is available
- `confidence`
- `riskLevel`: `low`, `medium`, `high`, or `blocked`
- `requiresConfirmation`
- `rationale`

permission tiers:

- `observe`: screen observation/explanation only; all OS action classes are blocked.
- `point`: visual pointing/overlay guidance only; all OS action classes are blocked.
- `confirmBeforeAction`: default; every non-blocked OS action requires explicit confirmation.
- `scopedAutopilot`: represents a future bounded task mode; low-risk navigation/control classes may be allowed inside scope, while text/app/hotkey/risky actions require confirmation.
- `fullControl`: represented for explicit user configuration only; it is never enabled by a plugin request and is not the default.

response rules:

- `actionMode` defaults to `confirmBeforeAction`.
- `permissionDecisions[]` can attach inert allow/requireConfirmation/block decisions to proposals.
- `safetyDecisions[]` can attach inert allow/requireConfirmation/block decisions with risk flags to proposals.
- blocked actions are represented as proposals with `riskLevel: blocked` and `status: blocked`; they still do not execute.
- blocked safety decisions set `forwardToExecutor=false` so a future execution pipeline cannot receive them.
- no native ui-framework fields, platform api payloads, provider payloads, or executor-specific fields are part of the proposal contract.

safety risk flags:

- `destructive`
- `payment`
- `purchase`
- `send_message`
- `credential_entry`
- `permission_prompt`
- `low_confidence`
- `blocked_risk`

### confirmation UX

When a proposal needs confirmation, Hermes receives a structured `confirmationRequest` with:

- `confirmationId`
- `proposalId`
- `actionSummary`
- `target`
- `risk`
- `reason`
- `expiresAt`

The user response is represented as a `confirmationResponse` with `decision`:

- `approved`
- `cancelled`
- `explanationRequested`
- `stale`

Rules:

- cancelled actions set `forwardToExecutor=false` and append an inert `confirmation_cancelled` audit event.
- explanation requests set `needsExplanation=true` and do not forward.
- stale proposals are rejected by expiry or proposal id mismatch.
- approved proposals are rechecked by `safety_policy.py` immediately before forwarding semantics are returned.
- even after approval, `forwardToExecutor=true` only when the fresh safety decision is `allow`.
- no executor is called from the Hermes-facing confirmation helper; native execution is a separate gated bridge call.

### executeAction

`executeAction` is the native execution request/result contract for approved desktop actions. Windows and macOS bridge backends implement it behind the same permission, safety, and confirmation gates. It supports:

- `click`
- `doubleClick`
- `typeText`
- `hotkey`
- `openApplication`
- `focusWindow`

request rules:

- include the original `actionProposal`.
- include a matching `permissionDecision`.
- include a matching `safetyDecision` with `decision: allow` and `forwardToExecutor: true`.
- include an approved `confirmationResponse` when permission or proposal state requires confirmation.
- blocked, missing, mismatched, stale, cancelled, or unapproved gates return `status: blocked` and are not forwarded to the executor.

execution result fields:

- `protocolVersion`
- `ok`
- `status`: `executed`, `blocked`, `cancelled`, or `error`
- `proposalId`
- `actionType`
- `result`
- `reason`
- `startedAt`
- `completedAt`
- `forwardedToExecutor`

### post-action verification

After an `executeAction` result with `status: executed`, the caller should run one fresh screen observation unless the execution result explicitly reports `result.verificationRequired: false`. Verification consumes provider-neutral observation flags and returns:

- `status: success` when the expected state is observed.
- `status: failed` for unchanged targets, unchanged screens, visible error dialogs, or app focus loss.
- `status: blockedByPrompt` when a permission/security prompt appears.
- `status: uncertain` when the screen changed but the expected state cannot be confirmed.
- `status: skipped` when verification is explicitly unnecessary or the action did not execute.

`failed`, `blockedByPrompt`, and `uncertain` set `continueAutomation: false`; `uncertain` also sets `requiresUserGuidance: true` so scoped automation stops instead of guessing.

### scoped autopilot sessions

Scoped autopilot is a bounded task mode, not full control. A session declares:

- `sessionId`
- task description
- allowed applications/windows
- allowed action types
- timeout
- maximum step count

Each proposed step is still checked by the `scopedAutopilot` permission tier and safety policy before it can be forwarded. High-risk, text, app-launch, or hotkey steps require confirmation. The session stops on timeout, max steps, scope violation, blocked action, failed/uncertain/prompt verification, or user cancellation. Audit records include `sessionId` so all steps can be grouped.

## examples

- `examples/capabilities.windows.json`
- `examples/observe.metadata-only.json`
- `examples/explain.success.json`
- `examples/point.success.json`
- `examples/point.low-confidence.json`
- `examples/action.low-risk-click.json`
- `examples/action.high-risk-destructive.json`
- `examples/action.blocked.json`
- `examples/permission.observe-blocks-action.json`
- `examples/permission.full-control-capability.json`
- `examples/confirmation.required.json`
- `examples/confirmation.cancelled.json`
- `examples/confirmation.approved-rechecked.json`
- `examples/confirmation.stale.json`
- `examples/action.execution.executed.json`
- `examples/autopilot.allowed-step.json`
- `examples/verification.success.json`
- `examples/verification.uncertain.json`

validate:

```bash
python3 scripts/validate_protocol.py
python3 -m unittest tests/test_protocol_contract.py -v
```

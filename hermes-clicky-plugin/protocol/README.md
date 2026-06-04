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
- os control is not part of v1.

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
  }
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
  "task": "explain what controls are visible"
}
```

response includes:

- `explanation`
- optional `regions[]` with labels, coordinates, and confidence

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

non-actionable response statuses include:

- `no_target`
- `low_confidence`
- `permission_denied`
- `bridge_unavailable`
- `error`

## examples

- `examples/capabilities.windows.json`
- `examples/observe.metadata-only.json`
- `examples/explain.success.json`
- `examples/point.success.json`
- `examples/point.low-confidence.json`

validate:

```bash
python3 scripts/validate_protocol.py
python3 -m unittest tests/test_protocol_contract.py -v
```

# Hermes bridge macOS observe/explain/point smoke

US-008 adds a narrow macOS JSON-RPC stdio bridge at:

```text
mac/hermes-clicky-bridge/clicky_macos_bridge.py
```

It does not launch the tray app, does not execute OS control, and keeps provider payloads out of Hermes responses.

## configure Hermes bridge command

```bash
export CLICKY_BRIDGE_COMMAND="python3 /Users/rick81/clicky/mac/hermes-clicky-bridge/clicky_macos_bridge.py"
```

## health

```bash
REQ='{"jsonrpc":"2.0","id":"mac-health","method":"rpc.health","params":{}}'
printf '%s\n' "$REQ" | python3 mac/hermes-clicky-bridge/clicky_macos_bridge.py
```

Expected: `status:"ok"`, `bridge:"macos"`.

## permissions

macOS permissions live in System Settings:

1. Privacy & Security → Screen Recording → allow Clicky / terminal / bridge host.
2. Privacy & Security → Accessibility → allow Clicky / terminal / bridge host.
3. Restart the host process after changes.

Deterministic permission fixtures for tests/manual checks:

```bash
export CLICKY_MAC_BRIDGE_SCREEN_RECORDING=1
export CLICKY_MAC_BRIDGE_ACCESSIBILITY=1
```

Unset those env vars to use real macOS probes.

## observe metadata only

```bash
REQ='{"jsonrpc":"2.0","id":"mac-observe","method":"clicky.observeScreen","params":{"imageMode":"metadataOnly"}}'
printf '%s\n' "$REQ" | CLICKY_MAC_BRIDGE_SCREEN_RECORDING=1 python3 mac/hermes-clicky-bridge/clicky_macos_bridge.py
```

Expected:

- `ok:true`
- `status:"observed"`
- `platform:"macos"`
- `coordinateSpace:"physical_pixels"`
- `displays[]` includes bounds, scale, coordinateScale
- no image `data` or `path` for `metadataOnly`

If Screen Recording is unavailable, expected error:

```json
{"code":"permission_denied","permission":"screen_capture","retryable":false}
```

## explain

```bash
export CLICKY_MAC_BRIDGE_EXPLANATION_RESPONSE='{"explanation":"The dialog asks for confirmation.","regions":[{"label":"confirm button","normalized":{"x":0.8,"y":0.75},"confidence":0.9}]}'
REQ='{"jsonrpc":"2.0","id":"mac-explain","method":"clicky.explainScreen","params":{"task":"explain visible dialog","observationId":"manual-obs"}}'
printf '%s\n' "$REQ" | CLICKY_MAC_BRIDGE_SCREEN_RECORDING=1 python3 mac/hermes-clicky-bridge/clicky_macos_bridge.py
```

Expected: `status:"explained"`, concise `explanation`, optional `regions`; no raw provider/model payload.

## point dry-run

```bash
export CLICKY_MAC_BRIDGE_POINT_RESPONSE='{"label":"confirm button","confidence":0.87,"normalized":{"x":0.8,"y":0.75},"physical":{"x":1440,"y":810,"screen":"macos-display-1"},"reasoning":"button is in the lower right"}'
REQ='{"jsonrpc":"2.0","id":"mac-point","method":"clicky.pointToTarget","params":{"target":"confirm button","renderOverlay":false}}'
printf '%s\n' "$REQ" | CLICKY_MAC_BRIDGE_SCREEN_RECORDING=1 CLICKY_MAC_BRIDGE_ACCESSIBILITY=1 python3 mac/hermes-clicky-bridge/clicky_macos_bridge.py
```

Expected: `status:"pointed"`, normalized + physical coordinates, `overlayRendered:false`.

## overlay note

The bridge has an overlay-rendered seam but does not launch the tray app implicitly. Real overlay rendering should be hosted by the native app/runtime and wired later through the same protocol boundary.

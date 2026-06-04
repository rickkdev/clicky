# Hermes Clicky plugin demo workflow

This is the V1 product demo path: the user asks where to click, Hermes calls `pointToTarget`, Clicky renders or attempts the pointer overlay, then Hermes replies with a concise explanation from `explainScreen`.

The demo is deterministic by default and uses `scripts/fake_bridge.py` unless a native bridge command is passed. That keeps normal test runs safe: no tray launch, no OS control, no real click/type/open-app execution.

## Windows demo path

Fake bridge / CI-safe:

```bash
python3 hermes-clicky-plugin/examples/demo_workflow.py \
  --bridge-command "python3 hermes-clicky-plugin/scripts/fake_bridge.py" \
  --platform windows \
  --target "settings" \
  --task "show me where to configure the repository"
```

Windows bridge path after building `windows/src/Clicky.Bridge`:

```bash
export CLICKY_BRIDGE_COMMAND='PATH="$HOME/.dotnet:$PATH" dotnet windows/src/Clicky.Bridge/bin/Debug/net8.0-windows10.0.19041.0/Clicky.Bridge.dll'
export CLICKY_BRIDGE_POINTING_RESPONSE='[POINT:480,270:Settings] The Settings tab is where repository configuration lives.'
export CLICKY_BRIDGE_EXPLANATION_RESPONSE='{"explanation":"The highlighted Settings tab opens the repository configuration page.","regions":[{"label":"Settings tab","normalized":{"x":0.5,"y":0.5},"confidence":0.91}]}'
python3 hermes-clicky-plugin/examples/demo_workflow.py \
  --bridge-command "$CLICKY_BRIDGE_COMMAND" \
  --platform windows \
  --target "Settings tab" \
  --task "show me where to configure the repository"
```

## macOS demo path

macOS is available now as a deterministic stdio bridge path:

```bash
export CLICKY_MAC_BRIDGE_SCREEN_RECORDING=1
export CLICKY_MAC_BRIDGE_ACCESSIBILITY=1
export CLICKY_MAC_BRIDGE_POINT_RESPONSE='{"label":"confirm button","confidence":0.87,"normalized":{"x":0.8,"y":0.75},"physical":{"x":1440,"y":810,"screen":"macos-display-1"},"reasoning":"button is in the lower right"}'
export CLICKY_MAC_BRIDGE_EXPLANATION_RESPONSE='{"explanation":"The highlighted confirm button accepts the dialog.","regions":[{"label":"confirm button","normalized":{"x":0.8,"y":0.75},"confidence":0.9}]}'
python3 hermes-clicky-plugin/examples/demo_workflow.py \
  --bridge-command "python3 mac/hermes-clicky-bridge/clicky_macos_bridge.py" \
  --platform macos \
  --target "confirm button" \
  --task "show me what to click to accept the dialog"
```

## Timing fields

The script emits stable timing keys for demo logs:

- `bridgeStartupMs` — JSON-RPC bridge health/startup roundtrip.
- `screenCaptureMs` — `observeScreen` roundtrip, including capture/metadata work.
- `modelResponseMs` — `pointToTarget` plus `explainScreen` model/semantic roundtrips.
- `overlayRenderMs` — overlay render timing placeholder; currently included in the point roundtrip because standalone bridges do not expose a separate overlay span.
- `totalMs` — full demo workflow duration.

Use `--json` for machine-readable output.

## Expected user-facing flow

1. User: “show me where to configure the repository.”
2. Hermes calls `observeScreen` for current screen context.
3. Hermes calls `pointToTarget` with `renderOverlay=true`.
4. Clicky returns target label, confidence, normalized and physical coordinates, and `overlayRendered`.
5. Hermes calls `explainScreen` to explain the highlighted target.
6. Hermes replies: “I rendered the pointer on Settings. The highlighted Settings tab opens the repository configuration page.”

## Known failures

Failure case: bridge unavailable
- Status: `bridge_unavailable`
- User behavior: Hermes says Clicky is not configured and asks the user to set `CLICKY_BRIDGE_COMMAND` or run the native bridge.

Failure case: screen capture permission denied
- Status: `permission_denied`, permission `screen_capture`
- User behavior: Hermes asks the user to grant Screen Recording/screen capture permission and retry.

Failure case: low confidence target
- Status: `low_confidence`
- User behavior: Hermes does not invent coordinates. It asks for a clearer target or runs observe/explain first.

Failure case: overlay unavailable
- Status: target may still be `pointed`, but `overlayRendered=false`
- User behavior: Hermes says it found the target and returns explanation, while noting no visual pointer was rendered.

## non-goals

- no clicking
- no typing
- no opening apps
- no scoped autopilot
- no full-control OS automation

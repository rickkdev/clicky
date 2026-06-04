# hermes-clicky-plugin

self-contained hermes plugin package for clicky.

this folder is the boundary. everything hermes needs to install, load, validate, and demo the plugin lives here.

native clicky code stays outside this folder:

- `../windows/` = windows native runtime backend
- `../mac/` = macos native runtime backend
- future windows bridge host: `../windows/src/Clicky.Bridge/`

v1 scope:

- get capabilities
- observe screen
- explain screen
- point to target
- optionally render clicky's pointer/annotation overlay

v1 non-goals:

- speech transcription
- tts playback
- cloudflare worker routing
- click/type/open-app os control
- action execution
- scoped autopilot engine
- rewriting clicky as native hermes code

## install locally

copy or symlink this folder into hermes plugins:

```bash
mkdir -p ~/.hermes/plugins
ln -s "$(pwd)/hermes-clicky-plugin" ~/.hermes/plugins/hermes-clicky-plugin
```

restart hermes. the plugin should register these tools:

- `get_clicky_capabilities`
- `observe_clicky_screen`
- `explain_clicky_screen`
- `point_clicky_target`

## current state

this is a contract-first skeleton with a deterministic stdio fake bridge at `scripts/fake_bridge.py`. without `CLICKY_BRIDGE_COMMAND`, tool calls return structured `bridge_unavailable` responses instead of pretending to work.

that is intentional. fake success from the real plugin path would be poison here.

## protocol

canonical schema:

- `protocol/schema.json`

examples:

- `protocol/examples/capabilities.windows.json`
- `protocol/examples/observe.metadata-only.json`
- `protocol/examples/explain.success.json`
- `protocol/examples/point.success.json`
- `protocol/examples/point.low-confidence.json`
- `protocol/examples/action.low-risk-click.json`
- `protocol/examples/action.high-risk-destructive.json`
- `protocol/examples/action.blocked.json`
- `protocol/examples/permission.observe-blocks-action.json`
- `protocol/examples/permission.full-control-capability.json`
- `protocol/examples/confirmation.required.json`
- `protocol/examples/confirmation.cancelled.json`
- `protocol/examples/confirmation.approved-rechecked.json`
- `protocol/examples/confirmation.stale.json`
- `protocol/examples/action.execution.executed.json`

validate:

```bash
python3 scripts/validate_protocol.py
```

## bridge plan

transport: json-rpc over stdio.

localhost/WebSocket are deferred: stdio avoids ports, firewall prompts, auth/listener lifecycle, and nondeterministic network cleanup while the bridge contract is still changing.

plugin side:

- `bridge_client.py`

native side:

- windows: `../windows/src/Clicky.Bridge/`
- macos: `../mac/hermes-clicky-bridge/clicky_macos_bridge.py`

## macOS bridge

Configure Hermes to use the macOS stdio bridge:

```bash
export CLICKY_BRIDGE_COMMAND="python3 /Users/rick81/clicky/mac/hermes-clicky-bridge/clicky_macos_bridge.py"
```

The macOS bridge supports `getCapabilities`, `observeScreen`, `explainScreen`, and `pointToTarget` through the shared protocol. It does not launch the tray app and does not execute OS control. Explanation and pointing use deterministic env seams until the native model host is wired:

- `CLICKY_MAC_BRIDGE_EXPLANATION_RESPONSE`
- `CLICKY_MAC_BRIDGE_POINT_RESPONSE`

Manual smoke checklist: `../mac/docs/hermes-bridge-macos-smoke.md`.

## macOS permissions

`get_clicky_capabilities(platform="macos")` reports permission state without launching the native app:

- Screen Recording gates screen capture, observe, explain, and point.
- Accessibility gates overlay rendering and any future OS-control path.
- OS control remains disabled with `phase_2_not_implemented` even when Accessibility is granted.

Enable permissions manually in System Settings:

1. Privacy & Security → Screen Recording → allow Clicky / the future bridge host.
2. Privacy & Security → Accessibility → allow Clicky / the future bridge host.
3. restart the host process after changing permissions; macOS TCC often requires restart.

For the standalone macOS bridge, granted permissions still require deterministic explanation/point env seams until the native model host is wired.

## demo workflow

Run the V1 point/explain product demo:

```bash
python3 hermes-clicky-plugin/examples/demo_workflow.py \
  --bridge-command "python3 hermes-clicky-plugin/scripts/fake_bridge.py" \
  --platform windows \
  --target "settings" \
  --task "show me where to configure the repository"
```

Full demo notes and failure cases: `examples/demo_workflow.md`.

## design rules

- do not expose wpf, win32, screencapturekit, swift, codex app-server, or provider payloads in hermes-facing responses.
- do not write screenshots to disk unless `imageMode=file` or debug mode is explicit.
- default `imageMode` is `metadataOnly`.
- low-confidence pointing returns `low_confidence`, not made-up coordinates.
- visual pointing results are not executable action proposals.
- action proposal examples are inert protocol data only; default `actionMode` / active permission tier is `confirmBeforeAction`.
- permission tiers are protocol/policy semantics only: `observe`, `point`, `confirmBeforeAction`, `scopedAutopilot`, `fullControl`.
- changing permission tier requires an explicit user-facing setting or command; plugin requests cannot silently enable `fullControl`.
- safety policy is a separate inert layer in `safety_policy.py`; it flags destructive actions, payments, purchases, sending messages/emails, credential entry, permission prompts, and low-confidence targets.
- confirmation UX is structured Hermes-facing state in `confirmation_state.py`; it builds concise redacted confirmation requests and handles approve/cancel/explain/stale responses without dialogs or execution.
- approved confirmations perform a fresh safety recheck before returning forwarding semantics; blocked rechecks set `forwardToExecutor=false`.
- blocked safety decisions set `forwardToExecutor=false` and can append inert audit records, but they do not write logs or call an executor.
- phase 2 os control execution is Windows-native only in `../windows/src/Clicky.Bridge/` for US-015. this package defines schema/docs only and does not execute click/type/open-app/hotkey/focus actions directly.

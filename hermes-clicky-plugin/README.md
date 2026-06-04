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
- scoped autopilot
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
- `protocol/examples/point.success.json`
- `protocol/examples/point.low-confidence.json`

validate:

```bash
python3 scripts/validate_protocol.py
```

## bridge plan

transport: json-rpc over stdio.

localhost/WebSocket are deferred: stdio avoids ports, firewall prompts, auth/listener lifecycle, and nondeterministic network cleanup while the bridge contract is still changing.

plugin side:

- `bridge_client.py`

native side later:

- windows: `../windows/src/Clicky.Bridge/`
- macos: not first; add after windows proves the contract.

## design rules

- do not expose wpf, win32, screencapturekit, swift, codex app-server, or provider payloads in hermes-facing responses.
- do not write screenshots to disk unless `imageMode=file` or debug mode is explicit.
- default `imageMode` is `metadataOnly`.
- low-confidence pointing returns `low_confidence`, not made-up coordinates.
- os control is phase 2. no click/type/open-app execution in v1.

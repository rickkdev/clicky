# Hermes bridge explainScreen smoke

US-006 keeps explanation-only turns out of speech/TTS and out of overlay rendering.
The standalone stdio bridge does not launch the tray app or any desktop overlay.

## deterministic stdio health

```bash
REQ='{"jsonrpc":"2.0","id":"smoke-health","method":"rpc.health","params":{}}'
printf '%s\n' "$REQ" | PATH="$HOME/.dotnet:$PATH" dotnet windows/src/Clicky.Bridge/bin/Debug/net8.0-windows10.0.19041.0/Clicky.Bridge.dll
```

Expected: JSON-RPC result with `status:"ok"`, `bridge:"windows"`.

## deterministic fake explain path

Use the plugin fake bridge for CI-safe explain smoke:

```bash
REQ='{"jsonrpc":"2.0","id":"smoke-explain","method":"clicky.explainScreen","params":{"task":"explain visible controls","observationId":"obs-smoke"}}'
printf '%s\n' "$REQ" | python3 hermes-clicky-plugin/scripts/fake_bridge.py
```

Expected: `ok:true`, `status:"explained"`, concise `explanation`, optional `regions`.

## standalone Windows bridge with configured fake model response

On Windows with screen capture available:

```powershell
$env:CLICKY_BRIDGE_EXPLANATION_RESPONSE='{"explanation":"The dialog asks for confirmation. The primary action is in the lower right.","regions":[{"label":"primary action","normalized":{"x":0.82,"y":0.84},"confidence":0.88}]}'
$req='{"jsonrpc":"2.0","id":"explain-1","method":"clicky.explainScreen","params":{"task":"explain what the visible dialog wants","observationId":"manual-obs"}}'
$req | dotnet windows/src/Clicky.Bridge/bin/Debug/net8.0-windows10.0.19041.0/Clicky.Bridge.dll
```

Expected: `status:"explained"`; no overlay window; no TTS/speech playback; no provider-specific raw model payload.
If capture permission is unavailable, expected error is `permission_denied` with `permission:"screen_capture"`.

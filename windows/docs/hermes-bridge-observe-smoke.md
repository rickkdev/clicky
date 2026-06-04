# Hermes bridge observe/point smoke checklist

US-004 wires `clicky.observeScreen` to the Windows capture seam through `windows/src/Clicky.Bridge`.
US-005 wires `clicky.pointToTarget` to the redesigned structured pointing adapter (`PointingTurnResult` / `PointIntent`) through the same bridge.

normal CI should use `WindowsObserveServiceTests` with fake capture providers. do not run compositor-dependent screen capture in CI.

## build/test on Windows

```powershell
cd windows
dotnet test tests/Clicky.Tests/Clicky.Tests.csproj --filter WindowsObserveServiceTests
```

## stdio smoke on Windows

```powershell
cd windows
$request = '{"jsonrpc":"2.0","id":"smoke-1","method":"clicky.observeScreen","params":{"imageMode":"metadataOnly","includeCursorScreenOnly":true}}'
$request | dotnet run --project src/Clicky.Bridge/Clicky.Bridge.csproj
```

expected:

- JSON-RPC response with `result.ok=true` and `result.status="observed"`.
- `displays[]` includes `id`, `bounds`, `scale`, `coordinateScale`, and `isCursorScreen`.
- `images[]` includes JPEG dimensions and `mode="metadataOnly"`.
- no screenshot file is written for metadata-only mode.

## file mode smoke

```powershell
$tmp = Join-Path $env:TEMP "clicky-hermes-smoke"
New-Item -ItemType Directory -Force $tmp | Out-Null
$request = '{"jsonrpc":"2.0","id":"smoke-2","method":"clicky.observeScreen","params":{"imageMode":"file","includeCursorScreenOnly":true,"outputDirectory":"' + ($tmp -replace '\\','\\') + '"}}'
$request | dotnet run --project src/Clicky.Bridge/Clicky.Bridge.csproj
Get-ChildItem $tmp
```

expected: exactly requested screenshot files under `$tmp`; no writes for `metadataOnly`.

## pointToTarget dry-run smoke

The standalone bridge does not launch the tray app or real overlay implicitly. For a deterministic bridge smoke, provide the redesigned structured result explicitly:

```powershell
$env:CLICKY_BRIDGE_POINTING_RESPONSE = '{"spokenText":"the button is here.","pointIntent":{"kind":"point","x":100,"y":50,"screen":1,"label":"button","confidence":"high"}}'
$request = '{"jsonrpc":"2.0","id":"smoke-3","method":"clicky.pointToTarget","params":{"target":"button","renderOverlay":false}}'
$request | dotnet run --project src/Clicky.Bridge/Clicky.Bridge.csproj
```

expected:

- JSON-RPC response with `result.status="pointed"`.
- response includes `normalized`, `physical`, `label`, `reasoning`, and `overlayRendered=false`.
- low confidence / none intents return `low_confidence` or `no_target` and no coordinates.

For real overlay demos, use `renderOverlay=true`; the stdio bridge now owns a Windows-only real overlay adapter that loads `Clicky.Overlay.dll` and renders the second cursor without launching the tray app. See `windows/docs/hermes-bridge-real-overlay-smoke.md`.

## permission/unavailable behavior

if Windows capture is unavailable, bridge returns a JSON-RPC error:

```json
{
  "code": "permission_denied",
  "message": "Windows screen capture unavailable: ...",
  "retryable": false,
  "permission": "screen_capture"
}
```

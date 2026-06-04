# Hermes bridge observe smoke checklist

US-004 wires `clicky.observeScreen` to the Windows capture seam through `windows/src/Clicky.Bridge`.

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

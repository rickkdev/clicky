# Windows Hermes bridge real overlay smoke

US-011A wires `clicky.pointToTarget` with `renderOverlay=true` to the real Clicky second-cursor overlay through `windows/src/Clicky.Overlay/`.

Scope:

- Windows only
- JSON-RPC stdio bridge only
- no tray auto-launch
- no clicks, typing, app launching, focus changes, hotkeys, or OS control
- deterministic model output supplied through `CLICKY_BRIDGE_POINTING_RESPONSE`

## Build

From the repo root on Windows:

```powershell
cd windows
 dotnet build .\src\Clicky.Bridge\Clicky.Bridge.csproj -c Debug
```

The bridge build must place `Clicky.Overlay.dll` beside `Clicky.Bridge.exe`. If it is missing, rebuild on Windows; the bridge loads the overlay assembly at runtime and reports `overlayRendered=false` if unavailable.

## Smoke command

Open a normal desktop with something obvious visible near the center of the primary screen. Then run this from the repo root:

```powershell
$env:CLICKY_BRIDGE_POINTING_RESPONSE = '{ "spokenText": "button is here.", "pointIntent": { "kind": "point", "x": 480, "y": 270, "screenNumber": 1, "label": "button" } }'
$env:CLICKY_BRIDGE_OVERLAY_LINGER_MS = '6500'
$request = '{"jsonrpc":"2.0","id":"smoke-1","method":"clicky.pointToTarget","params":{"target":"button","renderOverlay":true,"useRedesignedPointingProtocol":true}}'
$request | .\windows\src\Clicky.Bridge\bin\Debug\net8.0-windows10.0.19041.0\Clicky.Bridge.exe
```

Expected:

- a real Clicky blue second cursor/annotation appears visibly for about 6.5 seconds
- JSON response has `result.status: "pointed"`
- `result.physical` and `result.normalized` are populated
- `result.overlayRendered: true`

## Failure checks

If the response still has coordinates but `overlayRendered:false`, pointing worked but the overlay path was unavailable. Check:

- `Clicky.Overlay.dll` is beside `Clicky.Bridge.exe`
- the process is running on Windows desktop, not a headless/service session
- no policy blocks WPF overlay windows
- `CLICKY_BRIDGE_OVERLAY_LINGER_MS` is not `0`

`renderOverlay=false` should still return the same point coordinates and must not create any visible overlay.

# Clicky for Windows

This directory holds the Windows port of Clicky, an AI buddy that lives next
to the cursor, can see the screen, talk back through TTS, and fly to UI
elements it wants to point at.

The macOS reference implementation lives in `../mac/`. The Cloudflare Worker
that proxies the Anthropic, AssemblyAI, and ElevenLabs APIs is shared at
`../worker/` and is reused unchanged.

## Target stack

- **Language / Runtime:** C# 12 on .NET 8 (LTS)
- **UI:** WPF (transparent topmost overlay + tray-only host window). WPF was
  picked over WinUI 3 because click-through, per-monitor DPI, layered
  windows, and `WS_EX_TRANSPARENT` are battle-tested there.
- **Tray icon:** `H.NotifyIcon` (modern Win11-friendly NotifyIcon wrapper)
- **Global hotkey:** `SetWindowsHookEx(WH_KEYBOARD_LL)` low-level keyboard
  hook so modifier-only chords (Ctrl+Alt) work like the Mac CGEventTap.
- **Audio capture:** `NAudio.Wasapi` (`WasapiCapture` at the device's native
  format, then resampled to 16 kHz mono PCM16 for AssemblyAI).
- **Audio playback:** `NAudio` `Mp3FileReader` + `WaveOutEvent` for
  ElevenLabs MP3 playback.
- **Screen capture:** `Windows.Graphics.Capture` via CsWinRT, with
  per-display `GraphicsCaptureItem`s. Falls back to DXGI Desktop Duplication
  on Windows 10 builds without WGC permission prompts.
- **HTTP / SSE:** `System.Net.Http.HttpClient` with `HttpCompletionOption.ResponseHeadersRead`
  for streaming Claude responses.
- **WebSocket:** `System.Net.WebSockets.ClientWebSocket` for the AssemblyAI
  realtime streaming endpoint.
- **Auto-launch:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- **Auto-update:** WinSparkle bound through P/Invoke (mirrors Sparkle on Mac).
- **Analytics:** PostHog .NET SDK (mirrors `ClickyAnalytics.swift`).

## Project layout (planned)

```
windows/
  Clicky.sln
  src/
    Clicky.App/                 # WPF host, App.xaml, tray bootstrap
    Clicky.Companion/           # CompanionManager state machine
    Clicky.Audio/               # WASAPI capture + resampler + TTS playback
    Clicky.Capture/             # WGC multi-monitor screenshotter
    Clicky.Hotkey/              # Low-level keyboard hook + chord parser
    Clicky.Overlay/             # Transparent click-through cursor overlay
    Clicky.Api/                 # Claude SSE client + AssemblyAI ws client + ElevenLabs
    Clicky.Pointing/            # [POINT:x,y:label:screenN] parser + element locator
  tests/
    Clicky.Tests/
```

Each user story in `../prd.json` is sized to land in roughly one of these
projects so a single Ralph iteration can complete it end-to-end.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.x)
- Windows 10 version 1903+ or Windows 11
- (Optional) [Inno Setup 6](https://jrsoftware.org/isinfo.php) for building the installer

## Building

```powershell
# Restore + build (debug)
dotnet build windows/Clicky.sln

# Run tests
dotnet test windows/Clicky.sln
```

## Publishing a release

The release script publishes a framework-dependent single-file `.exe` and
optionally wraps it in an Inno Setup installer:

```powershell
# Full release (publish + installer if Inno Setup is installed)
powershell -ExecutionPolicy Bypass -File windows/scripts/release.ps1

# Publish only (skip installer)
powershell -ExecutionPolicy Bypass -File windows/scripts/release.ps1 -SkipInstaller

# Custom configuration / runtime
powershell -ExecutionPolicy Bypass -File windows/scripts/release.ps1 -Configuration Debug -Runtime win-arm64
```

Output locations:
- **Published files:** `windows/publish/win-x64/`
- **Installer:** `windows/installer/Setup_Clicky_0.1.0.exe`

### Manual publish (without the script)

```powershell
dotnet publish windows/src/Clicky.App/Clicky.App.csproj `
    -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true `
    -o windows/publish/win-x64
```

## Configuration

The worker base URL is read from `appsettings.json` at startup:

```json
{
  "WorkerBaseUrl": "https://your-worker-name.your-subdomain.workers.dev"
}
```

Edit this file (next to the `.exe`) to point at your own Cloudflare Worker deployment.

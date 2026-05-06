# Clicky Windows — Local Development Guide

## Prerequisites

- .NET 8 SDK
- Windows 10/11

## One-Time Setup

### 1. Windows Defender Exclusion

Smart App Control blocks unsigned exes. Run this once in an **admin PowerShell** to whitelist the project folder:

```powershell
Add-MpPreference -ExclusionPath "C:\Users\mail\Documents\workspace\clicky\clicky"
```

### 2. API Keys

Create a `.env` file in the repo root (it's gitignored — never committed):

```
CLICKY_ANTHROPIC_KEY=
CLICKY_ZAI_KEY=
CLICKY_ASSEMBLYAI_KEY=
CLICKY_ELEVENLABS_KEY=
```

Fill in your keys. On startup, the app reads `.env` and seeds any missing keys into the encrypted `secrets.bin`. Keys already in `secrets.bin` are not overwritten.

To force a re-seed (e.g. after rotating a key), delete `%APPDATA%\Clicky\secrets.bin` and restart.

## Build and Run

### Publish and double-click (recommended)

From the repo root:

```bash
./publish-dev.ps1
```

This script stops any running Clicky process, removes stale root-sidecar DLLs from older framework-dependent builds, and publishes a runnable self-contained single-file `Clicky.App.exe` directly into the repo root. It is large because the .NET runtime and native dependencies are bundled into the single file.

Equivalent manual command:

```bash
dotnet publish windows/src/Clicky.App/Clicky.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .
```

To publish and immediately launch:

```bash
./publish-dev.ps1 -Launch
```

### Run from source (alternative)

```bash
dotnet run --project windows/src/Clicky.App/Clicky.App.csproj -c Release
```

### Build only

```bash
dotnet build windows/Clicky.sln -c Release
```

### Run tests

```bash
dotnet test windows/Clicky.sln -c Release
```

## Version

Single source of truth:

```
windows/src/Clicky.App/Clicky.App.csproj  →  <Version>X.Y.Z</Version>
```

Shows in tray tooltip, tray menu, and Settings window footer. If `windows/scripts/clicky-installer.iss` has a hardcoded version, update it to match.

Bump policy: patch for bug fixes, minor for features, major for breaking changes. Don't bump for every dev build.

## File Layout

```
clicky/
  .env                       # API keys (gitignored, never committed)
  Clicky.App.exe             # Published single-file exe (gitignored build output)
  dev.md                     # This file
  windows/
    src/
      Clicky.App/            # WPF host, tray icon, settings window
      Clicky.Api/            # LLM clients, TTS, STT
      Clicky.Companion/      # Pipeline, SecretsStore, SettingsStore
      Clicky.Capture/        # Screen capture (WGC + GDI fallback)
      Clicky.Overlay/        # Transparent overlay, blue cursor animation
      Clicky.Pointing/       # [POINT:x,y] tag parsing, coordinate conversion
      Clicky.Hotkey/         # Global push-to-talk hook
      Clicky.Audio/          # Microphone recording
    tests/
      Clicky.Tests/
  mac/                       # Swift/Xcode reference (do not edit)
  worker/                    # Legacy Cloudflare proxy (Mac only, do not edit)
```

## Runtime Config

All under `%APPDATA%\Clicky\`:

| File | Contents |
|------|----------|
| `settings.json` | Provider, model, voice ID, device IDs, onboarding state |
| `secrets.bin` | API keys encrypted with DPAPI (per-user, not portable) |
| `debug.log` | Timestamped diagnostic log (latency, errors) |

## Troubleshooting

**Smart App Control blocks the exe:** Run the Defender exclusion command (see One-Time Setup).

**App is blocked after a rebuild even with the exclusion:** Re-run `./publish-dev.ps1`. It cleans stale root `Clicky.App.dll` / sidecar files that can cause Smart App Control to block the app by forcing the single-file exe to load unsigned local DLLs.

**Keys lost / Settings window keeps opening:** Delete `%APPDATA%\Clicky\secrets.bin` and restart — keys re-seed from `.env`.

**No audio:** Check `%APPDATA%\Clicky\debug.log`. Common causes: missing ElevenLabs key, wrong speaker device in Settings.

**Cursor points at wrong spot:** Check debug.log. Test at 100%, 150%, and 200% DPI scaling.

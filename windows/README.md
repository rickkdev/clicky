# Clicky for Windows

Clicky is an AI buddy that lives in your system tray. Hold a keyboard shortcut,
talk, and Clicky sees your screen, answers out loud, and can even point at
things with a little blue cursor. It's like having a knowledgeable friend
sitting next to you.

## What you need

Clicky runs entirely on your machine using your own API keys. Nothing is
shared, nothing is uploaded to Clicky's servers. You'll need accounts (and
keys) for three services:

| Service | What it does | Get a key |
|---------|-------------|-----------|
| [Anthropic](https://console.anthropic.com/settings/keys) **or** [z.ai](https://z.ai/manage-apikey/apikey-list) | Powers the AI (Claude or GLM) | ~$0.01–0.05 per conversation turn |
| [AssemblyAI](https://www.assemblyai.com/app/account) | Turns your voice into text in real time | Free tier available |
| [ElevenLabs](https://elevenlabs.io/app/settings/api-keys) | Speaks Clicky's replies out loud | Free tier available |

You can use Claude (Anthropic) or GLM (z.ai) as the AI brain, or both — switch
between them any time from the tray menu without restarting.

## Installing

1. Download the latest `Setup_Clicky_x.x.x.exe` from [Releases](https://github.com/julianjear/makesomething-mac-app/releases)
2. Run the installer — it takes about 10 seconds
3. Clicky opens automatically and walks you through entering your API keys
4. Grant microphone and screen capture permissions when prompted
5. Hold **Ctrl+Alt** and talk — Clicky is listening

Your API keys are encrypted with your Windows account credentials (DPAPI) and
stored only on your PC. Clicky never sends them anywhere.

## Switching AI models at runtime

Right-click the Clicky tray icon → **Model** to switch between:

- Claude Sonnet 4.6, Haiku 4.5, Opus 4.6 (Anthropic)
- GLM-4.6V, GLM-4.5V (z.ai)

The switch takes effect immediately — no restart needed.

## System requirements

- Windows 10 version 1903 or later (Windows 11 recommended)
- .NET 8 Runtime (the installer will prompt you if it's missing)
- A working microphone

## About the `worker/` directory

The `worker/` directory in the repo contains a Cloudflare Worker proxy used by
the Mac reference implementation. The Windows build talks directly to each
service's API — the worker is not used and does not need to be deployed.

## Building from source

If you want to hack on Clicky yourself:

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- (Optional) [Inno Setup 6](https://jrsoftware.org/isinfo.php) for building the installer

### Build and test

```powershell
dotnet build windows/Clicky.sln
dotnet test windows/Clicky.sln
```

### Publish a release

```powershell
powershell -ExecutionPolicy Bypass -File windows/scripts/release.ps1
```

Output:
- **Published files:** `windows/publish/win-x64/`
- **Installer:** `windows/installer/Setup_Clicky_0.1.0.exe`

## Project layout

```
windows/
  Clicky.sln
  src/
    Clicky.App/          # WPF host, tray icon, settings window
    Clicky.Companion/    # Push-to-talk state machine, settings/secrets stores
    Clicky.Audio/        # Microphone capture + resampler
    Clicky.Capture/      # Multi-monitor screenshot capture
    Clicky.Hotkey/       # Global keyboard hook (Ctrl+Alt chord)
    Clicky.Overlay/      # Transparent click-through cursor overlay
    Clicky.Api/          # Anthropic, z.ai, AssemblyAI, ElevenLabs clients
    Clicky.Pointing/     # [POINT:x,y:label] tag parser
  tests/
    Clicky.Tests/
```

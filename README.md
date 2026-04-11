# Hi, this is Clicky.
It's an AI buddy that lives next to your cursor. It can see your screen, talk to you, and even point at stuff. Kinda like having a real teacher next to you.

Download it [here](https://www.clicky.so/) for free.

Here's the [original tweet](https://x.com/FarzaTV/status/2041314633978659092) that kinda blew up for a demo for more context.

![Clicky — an ai buddy that lives on your mac](clicky-demo.gif)

This is the open-source version of Clicky for those that want to hack on it, build their own features, or just see how it works under the hood.

## Platforms

| Platform | Status | Details |
|----------|--------|---------|
| **Windows** | Shipping | Bring-your-own-keys, runs entirely on your machine. See [windows/README.md](windows/README.md) |
| **macOS** | Shipping | Uses a Cloudflare Worker proxy for API keys. See setup below |

### Windows — quick start

1. Download the installer from [Releases](https://github.com/julianjear/makesomething-mac-app/releases)
2. Run it, paste your API keys (Anthropic or z.ai + AssemblyAI + ElevenLabs)
3. Hold **Ctrl+Alt** and talk

Keys are encrypted with Windows DPAPI and never leave your machine. You can
switch between Claude and GLM at runtime from the tray menu. See
[windows/README.md](windows/README.md) for full details, building from source,
and system requirements.

### macOS — quick start

The fastest way to get this running is with [Claude Code](https://docs.anthropic.com/en/docs/claude-code).

Once you get Claude running, paste this:

```
Hi Claude.

Clone https://github.com/farzaa/clicky.git into my current directory.

Then read the CLAUDE.md. I want to get Clicky running locally on my Mac.

Help me set up everything — the Cloudflare Worker with my own API keys, the proxy URLs, and getting it building in Xcode. Walk me through it.
```

That's it. It'll clone the repo, read the docs, and walk you through the whole setup. Once you're running you can just keep talking to it — build features, fix bugs, whatever. Go crazy.

## Manual macOS setup

If you want to do it yourself, here's the deal.

### Prerequisites

- macOS 14.2+ (for ScreenCaptureKit)
- Xcode 15+
- Node.js 18+ (for the Cloudflare Worker)
- A [Cloudflare](https://cloudflare.com) account (free tier works)
- API keys for: [Anthropic](https://console.anthropic.com), [AssemblyAI](https://www.assemblyai.com), [ElevenLabs](https://elevenlabs.io)

### 1. Set up the Cloudflare Worker

The Worker is a tiny proxy that holds your API keys. The Mac app talks to the Worker, the Worker talks to the APIs. This way your keys never ship in the app binary.

```bash
cd worker
npm install
```

Now add your secrets. Wrangler will prompt you to paste each one:

```bash
npx wrangler secret put ANTHROPIC_API_KEY
npx wrangler secret put ASSEMBLYAI_API_KEY
npx wrangler secret put ELEVENLABS_API_KEY
```

For the ElevenLabs voice ID, open `wrangler.toml` and set it there (it's not sensitive):

```toml
[vars]
ELEVENLABS_VOICE_ID = "your-voice-id-here"
```

Deploy it:

```bash
npx wrangler deploy
```

It'll give you a URL like `https://your-worker-name.your-subdomain.workers.dev`. Copy that.

### 2. Run the Worker locally (for development)

If you want to test changes to the Worker without deploying:

```bash
cd worker
npx wrangler dev
```

This starts a local server (usually `http://localhost:8787`) that behaves exactly like the deployed Worker. You'll need to create a `.dev.vars` file in the `worker/` directory with your keys:

```
ANTHROPIC_API_KEY=sk-ant-...
ASSEMBLYAI_API_KEY=...
ELEVENLABS_API_KEY=...
ELEVENLABS_VOICE_ID=...
```

Then update the proxy URLs in the Swift code to point to `http://localhost:8787` instead of the deployed Worker URL while developing. Grep for `clicky-proxy` to find them all.

### 3. Update the proxy URLs in the app

The app has the Worker URL hardcoded in a few places. Search for `your-worker-name.your-subdomain.workers.dev` and replace it with your Worker URL:

```bash
grep -r "clicky-proxy" leanring-buddy/
```

You'll find it in:
- `CompanionManager.swift` — Claude chat + ElevenLabs TTS
- `AssemblyAIStreamingTranscriptionProvider.swift` — AssemblyAI token endpoint

### 4. Open in Xcode and run

```bash
open leanring-buddy.xcodeproj
```

In Xcode:
1. Select the `leanring-buddy` scheme (yes, the typo is intentional, long story)
2. Set your signing team under Signing & Capabilities
3. Hit **Cmd + R** to build and run

The app will appear in your menu bar (not the dock). Click the icon to open the panel, grant the permissions it asks for, and you're good.

### Permissions the app needs

- **Microphone** — for push-to-talk voice capture
- **Accessibility** — for the global keyboard shortcut (Control + Option)
- **Screen Recording** — for taking screenshots when you use the hotkey
- **Screen Content** — for ScreenCaptureKit access

## Architecture

If you want the full technical breakdown, read `CLAUDE.md`. But here's the short version:

**Menu bar / system tray app** with a control panel and a full-screen transparent cursor overlay. Push-to-talk streams audio to AssemblyAI for real-time transcription, sends the transcript + screenshots to an LLM (Claude or GLM) via streaming, and plays the response through ElevenLabs TTS. The LLM can embed `[POINT:x,y:label:screenN]` tags in its responses to make the blue cursor fly to specific UI elements across multiple monitors.

- **Mac:** APIs proxied through a Cloudflare Worker (`worker/`)
- **Windows:** Direct-to-provider with encrypted local key storage (no worker needed)

## Project structure

```
leanring-buddy/          # Swift source (macOS, yes the typo stays)
windows/                 # C# / .NET 8 / WPF (Windows)
worker/                  # Cloudflare Worker proxy (used by Mac only)
CLAUDE.md                # Full architecture doc (agents read this)
```

## Contributing

PRs welcome. If you're using Claude Code, it already knows the codebase — just tell it what you want to build and point it at `CLAUDE.md`.

Got feedback? DM me on X [@farzatv](https://x.com/farzatv).

# Ralph Agent Instructions

## Overview

Ralph is an autonomous AI agent loop that runs AI coding tools (Amp or Claude Code) repeatedly until all PRD items are complete. Each iteration is a fresh instance with clean context.

## Commands

```bash
# Run Ralph with Claude Code
./ralph.sh --tool claude [max_iterations]
```

## Key Files

- `ralph.sh` - The bash loop that spawns fresh AI instances (supports `--tool amp` or `--tool claude`)
- `prompt.md` - Instructions given to each AMP instance
- `CLAUDE.md` - Instructions given to each Claude Code instance
- `CODEX.md` - Instructions given to each Codex instance when Ralph is run against Codex
- `prd.json` - PRD formattion built with React Flow. It's designed for presentations - click through to reveal each step with animations.

## Patterns

- Each iteration spawns a fresh AI instance (Amp or Claude Code) with clean context
- Memory persists via git history, `progress.txt`, and `prd.json`
- Stories should be small enough to complete in one context window
- Always update AGENTS.md with discovered patterns for future iterations
- Keep `CODEX.md` aligned with `CLAUDE.md` when the Ralph loop instructions change so Codex runs follow the same workflow
- For Windows pointing regressions, use the tray `Desktop Smoke Test` fixture and inspect `%APPDATA%\Clicky\point-debug` plus `%APPDATA%\Clicky\debug.log`; keep compositor-dependent checks out of normal CI.
- Keep default Windows tests deterministic: use fake provider/capture/overlay seams for full-turn coverage, and reserve visible desktop/WPF compositor validation for the tray smoke fixture.
- Windows semantic pointing is being reset in PRD US-045 through US-050. Do not add more verifier/locator/prompt layers to the current implementation; first delete the failed layered stack, then rebuild from the documented protocol and real screenshot fixture evaluation.
- The Windows pointing redesign contract is `windows/docs/pointing-redesign.md`; follow it before changing semantic pointing behavior.
- The redesigned Windows pointing implementation is behind `CompanionManager`'s `useRedesignedPointingProtocol` constructor switch. `Clicky.App` enables it by default via `App.UseRedesignedPointingProtocolByDefault`; keep the switch for deterministic tests/comparison, and use `PointingTurnResult` / `PointIntent` as the single point-decision source in that path.
- Windows turn timing evidence is logged with `[TURN]` lines in `%APPDATA%\Clicky\debug.log`. Use the tray `Provider Timing Diagnostics` item to compare text-only LLM, image LLM, capture, and TTS timings before blaming overlay rendering or model vision.
- Semantic pointing fixture evidence lives under `windows/tests/Clicky.Tests/Fixtures/pointing/`; add real screenshot-style PNGs plus allowed/distractor polygons there, and run `SemanticPointingEvaluationTests` before changing map/UI pointing behavior.

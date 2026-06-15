# Hermes bridge end-to-end desktop task smoke — macOS

This is an opt-in real desktop smoke fixture. default CI must use fake providers/fake executors only.

## Safety contract

- opt-in only: set `CLICKY_RUN_DESKTOP_SMOKE=1` before running the real fixture.
- harmless app only: TextEdit.
- no purchases, no credentials, no real account messages, no destructive actions.
- no tray auto-launch. start the bridge/app under test yourself.
- if the screen does not match the expected TextEdit fixture, stop.

## Fixture workflow

1. open TextEdit through the approved `openApplication` action.
2. focus a new Untitled TextEdit document.
3. click the document editing area.
4. type `clicky smoke macos`.
5. run post-action observation/verification and require the expected text to be visible.

The same workflow is represented in `hermes-clicky-plugin/smoke_fixtures.py` so default CI can prove the fixture contract in fake mode without touching the desktop.

## Fake/default CI command

```bash
python3 -m unittest hermes-clicky-plugin/tests/test_desktop_smoke_fixtures.py -q
```

Expected baseline on this branch: fake macOS fixture passes 5/5 steps, `desktopActionsExecuted=false`.

## Real opt-in command

From a macOS desktop session with Screen Recording and Accessibility permissions granted:

```bash
export CLICKY_RUN_DESKTOP_SMOKE=1
export CLICKY_MAC_BRIDGE_COMMAND="python3 mac/hermes-clicky-bridge/clicky_macos_bridge.py"
python3 hermes-clicky-plugin/scripts/run_desktop_smoke.py --platform macos --real
```

If `run_desktop_smoke.py` is not present in the checkout, use the fixture steps above with the bridge JSON-RPC methods directly. do not run ad-hoc clicks outside the listed harmless TextEdit workflow.

## Logs

- `~/Library/Logs/Clicky/hermes-smoke/macos-basic-task.jsonl`
- `~/Library/Logs/Clicky/debug.log`

Record pass/fail, bridge command, Clicky version, macOS version, permission state, and any hardware limitations in `progress.txt` after a real run.

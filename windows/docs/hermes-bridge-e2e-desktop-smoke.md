# Hermes bridge end-to-end desktop task smoke — Windows

This is an opt-in real desktop smoke fixture. default CI must use fake providers/fake executors only.

## Safety contract

- opt-in only: set `CLICKY_RUN_DESKTOP_SMOKE=1` before running the real fixture.
- harmless app only: Notepad.
- no purchases, no credentials, no real account messages, no destructive actions.
- no tray auto-launch. start the bridge/app under test yourself.
- if the screen does not match the expected Notepad fixture, stop.

## Fixture workflow

1. open Notepad through the approved `openApplication` action.
2. focus the Untitled Notepad window.
3. click the document editing area.
4. type `clicky smoke windows`.
5. run post-action observation/verification and require the expected text to be visible.

The same workflow is represented in `hermes-clicky-plugin/smoke_fixtures.py` so default CI can prove the fixture contract in fake mode without touching the desktop.

## Fake/default CI command

```bash
python3 -m unittest hermes-clicky-plugin/tests/test_desktop_smoke_fixtures.py -q
```

Expected baseline on this branch: fake Windows fixture passes 5/5 steps, `desktopActionsExecuted=false`.

## Real opt-in command

From a Windows desktop session with the bridge built and Accessibility/input permissions available:

```powershell
$env:CLICKY_RUN_DESKTOP_SMOKE = "1"
$env:CLICKY_WINDOWS_BRIDGE_COMMAND = "dotnet run --project windows/src/Clicky.Bridge/Clicky.Bridge.csproj -c Release"
python hermes-clicky-plugin\scripts\run_desktop_smoke.py --platform windows --real
```

If `run_desktop_smoke.py` is not present in the checkout, use the fixture steps above with the bridge JSON-RPC methods directly. do not run ad-hoc clicks outside the listed harmless Notepad workflow.

## Logs

- `%APPDATA%\Clicky\debug.log`
- `%APPDATA%\Clicky\hermes-smoke\windows-basic-task.jsonl`

Record pass/fail, bridge command, Clicky version, Windows version, and any permission/compositor limitations in `progress.txt` after a real run.

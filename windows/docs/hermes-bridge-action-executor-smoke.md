# Windows Hermes bridge action executor smoke checklist

opt-in only. do not run in default CI.

purpose: verify `clicky.executeAction` real desktop execution for harmless actions after permission, safety, and confirmation gates have passed.

## safety rules

- use Notepad or another harmless local app only.
- do not use real accounts, payment pages, purchase flows, delete dialogs, permission prompts, credentials, tokens, or messages.
- close sensitive apps before testing.
- keep `CLICKY_PERMISSION_TIER` explicit for the bridge process if testing full-control behavior.
- every request must include matching permission and safety decisions; confirmation-required actions must include an approved confirmation response.

## setup

1. build the bridge:
   ```powershell
   dotnet build windows\src\Clicky.Bridge\Clicky.Bridge.csproj -c Release
   ```
2. run the bridge as a one-shot stdio JSON-RPC process.
3. send only one JSON-RPC line per process invocation.

## smoke cases

### open application

request: `clicky.executeAction` with `actionType=openApplication`, `application=notepad.exe`, permission `allow`, safety `allow/forwardToExecutor=true`.

expected:

- Notepad opens.
- result has `ok=true`, `status=executed`, `actionType=openApplication`, `forwardedToExecutor=true`.

### focus window

request: `actionType=focusWindow`, `nativeSelector.kind=windowTitle`, `nativeSelector.value=Notepad`.

expected:

- Notepad becomes foreground window.
- result has `status=executed` and `result.focused=true` when Windows accepts foreground focus.

### type benign text

request: `actionType=typeText`, `inputPreview=hello from clicky smoke test` with Notepad focused.

expected:

- Notepad receives exactly the benign text.
- result does not echo raw text; it reports `charactersTyped` and `redactedText=true`.

### harmless hotkey

request: `actionType=hotkey`, `hotkey=["Ctrl","A"]` with Notepad focused.

expected:

- text selection changes in Notepad.
- result reports `keyCount` only, not native key internals.

### click / doubleClick

request: `actionType=click` or `doubleClick` with coordinates inside the Notepad editor area.

expected:

- cursor/caret moves or selection behavior changes harmlessly.
- result reports `clickCount`, `coordinateSpace=physical_pixels`, and `screen`.

### blocked gate check

repeat any action with missing permission decision or safety `decision=block`.

expected:

- no desktop action happens.
- result has `ok=false`, `status=blocked`, `forwardedToExecutor=false`, and a concise reason.

## evidence to record

- exact bridge build command and result.
- exact action types tested.
- returned JSON execution results.
- whether any Windows focus restrictions affected `focusWindow`.
- confirm no raw credential/full typed-secret content appears in result JSON.

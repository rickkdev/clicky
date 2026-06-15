# macOS Hermes bridge action executor smoke

purpose: verify `clicky.executeAction` real desktop execution for harmless approved actions on macOS after permission, safety, and confirmation gates have passed.

normal CI must not run this. use the deterministic fake executor tests instead.

## prerequisites

- macOS host with Clicky repo checkout.
- Terminal or the bridge host has Accessibility permission:
  - System Settings → Privacy & Security → Accessibility
  - enable the terminal app running the bridge.
- For observe/explain/point smoke, Screen Recording may also be needed, but `executeAction` itself requires Accessibility.
- no credentials, purchases, destructive actions, messages, or real account workflows.

## bridge command

```bash
python3 mac/hermes-clicky-bridge/clicky_macos_bridge.py
```

For Hermes:

```bash
CLICKY_BRIDGE_COMMAND="python3 /Users/rick81/clicky/mac/hermes-clicky-bridge/clicky_macos_bridge.py" hermes chat --toolsets clicky
```

## safe smoke sequence

1. open TextEdit

request: `clicky.executeAction` with `actionType=openApplication`, `application=TextEdit`, permission decision `allow`, safety decision `allow`, `forwardToExecutor=true`.

expected: result `ok=true`, `status=executed`, `forwardedToExecutor=true`.

2. type harmless text

request: `actionType=typeText`, `inputPreview="hello from clicky hermes smoke"`, same gates.

expected: TextEdit receives the text. result is structured and does not echo raw typed text.

3. hotkey select all

request: `actionType=hotkey`, `hotkey=["Command", "A"]`, same gates.

expected: text selection changes. result `executed`.

4. coordinate click fallback

request: `actionType=click`, coordinates inside the TextEdit document, same gates.

expected: cursor/focus changes. result `executed`.

5. focus window

request: `actionType=focusWindow`, `nativeSelector={"kind":"windowTitle","value":"TextEdit"}`, same gates.

expected: TextEdit becomes frontmost where System Events can identify it.

## expected refusal checks

- remove Accessibility permission: `clicky.executeAction` returns JSON-RPC error `permission_denied` with `permission=accessibility`.
- omit `permissionDecision`: result `status=blocked`, `reason="missing permission decision"`, `forwardedToExecutor=false`.
- use blocked safety decision: result `status=blocked`, `reason="safety decision did not allow forwarding"`, no OS action.
- require confirmation without approved confirmation: result `status=blocked`, `reason="approved confirmation required"`, no OS action.

## notes

The macOS executor uses Accessibility/System Events for click, type, hotkey, and focus paths, and `/usr/bin/open -a` for app launch. Coordinate clicks are fallback behavior; semantic/native selectors should be preferred when reliable.

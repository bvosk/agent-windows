---
name: agent-windows
description: Windows desktop UI automation for agents via the agent-windows CLI. Read this before running any agent-windows commands. Covers the snapshot-and-ref workflow, attaching to and launching Windows applications, interacting with elements (click, fill, press, select, expand, toggle, scroll), waiting for UI state, taking screenshots, the REPL/batch fast path, running parallel sessions, Windows-specific gotchas (elevation, packaged apps, focus), and troubleshooting common failures. Use when the user asks to automate a Windows desktop application, click a button in an app, fill a native form or dialog, drive a Win32/WPF/WinForms/WinUI app, read text from a window, take a screenshot of an application, or test a desktop UI — even if they don't mention UI Automation or agent-windows by name. Not for web pages or browser automation (use a browser tool for those).
allowed-tools: Bash(agent-windows:*)
---

Windows-native UI automation CLI for AI agents, built on Microsoft UI Automation. Accessibility-tree snapshots with compact `@eN` refs let agents drive desktop apps in a few hundred tokens instead of pixel-hunting screenshots.

## The core loop

```
agent-windows attach --window "Notepad"   # 1. Attach to a window (or launch one)
agent-windows snapshot -i                 # 2. See what's in it (interactive elements only)
agent-windows activate @e5                # 3. Prefer semantic activation
agent-windows snapshot -i                 # 4. Re-snapshot after the UI changes
```

Refs like `@e1` are assigned fresh on every snapshot and become **stale the moment you take the next snapshot** — a stale ref returns a `stale-ref` error, never silently hits the wrong element. After any action that changes the UI (opening a menu, a dialog appearing, navigating), re-snapshot before acting again.

The first command auto-starts a background daemon that owns the attached window and the element table; you never manage it except `agent-windows daemon stop` when done.

## Quickstart

```
dotnet tool install -g AgentWindows       # needs the .NET 10 runtime
agent-windows list                        # what windows are on the desktop?
agent-windows attach --window "Calculator"
agent-windows snapshot -i
agent-windows screenshot calc.png
agent-windows daemon stop
```

(A self-contained `agent-windows.exe` build needs no .NET install — any copy on PATH works the same.)

## Attaching to apps

```
agent-windows list                             # windows: title, pid, process, hwnd, elevation
agent-windows attach --window "notepad"        # title substring, case-insensitive
agent-windows attach --pid 1234                # by process id
agent-windows attach --hwnd 65842              # by native handle
agent-windows launch --app "C:\path\app.exe" --args "--flag"   # launch + attach
```

`launch` fails for packaged (Store/UWP) stub executables that hand off to another process — for example `notepad.exe` on Windows 11. Start those yourself (e.g. `Start-Process notepad`), then `attach --window`.

## Reading a window

```
agent-windows snapshot                    # full accessibility tree (verbose)
agent-windows snapshot -i                 # interactive elements only (preferred)
agent-windows snapshot --depth 3          # limit tree depth
agent-windows snapshot --scope @e7        # only the subtree under a previous ref
agent-windows snapshot --json             # machine-readable envelope
agent-windows find --automation-id Save   # fast provider-side lookup with a live ref
```

Snapshot lines look like:

```
- window "Untitled - Notepad" [@e21]
  - document "Text editor" focused [@e1]
  - menuitem "File" automationId=File collapsed [@e5]
```

`automationId=` is shown when present — it is the most stable identifier Windows apps offer. Prefer elements that have one when disambiguating.

## Interacting

```
agent-windows activate @e1                # semantic Invoke/Toggle/Select/Expand (preferred)
agent-windows click @e1                   # click (retries until enabled + on-screen)
agent-windows click @e1 --double          # double-click
agent-windows click @e1 --right           # right-click (context menu)
agent-windows click --at 640,360          # raw coordinates — escape hatch only
agent-windows fill @e2 "hello"            # replace the value of an editable element
agent-windows press Enter                 # key or chord: Enter, Ctrl+S, Ctrl+Shift+Tab
agent-windows select @e4 "Item name"      # combo box, list, or tab control
agent-windows expand @e5                  # expand a menu / tree item / combo box
agent-windows expand @e5 --collapse       # collapse it
agent-windows toggle @e3 --on             # checkbox; --on/--off are idempotent
agent-windows scroll down @e6 --amount 5  # ScrollPattern with wheel fallback
```

Prefer `activate` for buttons, links, menu items, checkboxes, selectable items, and expandable controls. It avoids pointer movement and physical click timing. If the control returns `pattern-unsupported`, fall back to `click`.

Action commands take `--timeout <ms>` (default 10000) and retry until the element is actionable — you rarely need an explicit wait before acting on a ref.

**Focus matters on Windows:** pattern-based actions (`fill`, `toggle`, `select`, `expand`) work even when the window is in the background; `press` and keyboard fallbacks need the window foreground. Run `agent-windows window focus` before sending keys.

## Waiting

```
agent-windows wait @e9                       # until the element is present
agent-windows wait --text "Saved"            # until an element with this name appears
agent-windows wait --text "Loading" --gone   # until it disappears
agent-windows wait @e9 --gone --timeout 30000
```

Agents fail more often from bad waits than from bad selectors. Prefer `wait --text` on a concrete UI change over sleeping.

## Common workflows

### Drive a menu

Menus render their items only after expanding, so re-snapshot in between:

```
agent-windows snapshot -i
agent-windows expand @e5                  # the "File" menu item
agent-windows snapshot -i
agent-windows click @e31                  # "Save As..." in the fresh snapshot
```

### Fill a dialog

```
agent-windows wait --text "Save As"
agent-windows snapshot -i
agent-windows fill @e40 "C:\temp\out.txt"
agent-windows click @e42
agent-windows wait --text "Save As" --gone
```

### Screenshot

```
agent-windows screenshot window.png            # the target window
agent-windows screenshot part.png --ref @e7    # just one element
```

### Manage the window

```
agent-windows window focus
agent-windows window move --x 0 --y 0
agent-windows window resize --width 1280 --height 800
agent-windows window maximize      # also: minimize, restore
agent-windows close                # close the target window
agent-windows close --force        # kill the process
agent-windows status               # daemon pid, target, snapshot generation, ref count
```

### Run parallel sessions

Each `--session` gets its own isolated daemon (also settable via `AGENT_WINDOWS_SESSION`):

```
agent-windows --session a attach --window "App One"
agent-windows --session b attach --window "App Two"
agent-windows --session a snapshot -i
```

## REPL / batch mode — use for multi-step sequences

Every one-shot command pays ~130ms of process startup. For anything beyond 2–3 commands, run `agent-windows repl`: one process, a persistent daemon connection, one command per stdin line, exactly one JSON envelope per stdout line (~8ms per command instead of ~200ms):

```
agent-windows repl
snapshot -i
{"ok":true,"payload":{"kind":"snapshot",...},"elapsedMs":50.1}
click @e5
{"ok":true,"payload":{"kind":"ack","detail":"clicked"},"elapsedMs":2.3}
exit
```

Rules: lines are CLI syntax (quotes supported) or a raw JSON request when the line starts with `{`; `#` comments and blank lines are skipped; `exit`/`quit` or EOF end the session, so `agent-windows repl < script.txt` works for batches; the **first failing command ends the session with exit code 1** — re-snapshot and restart the loop. Nothing but response envelopes is ever written to stdout. One REPL holds its session's daemon connection, so run parallel REPLs on separate `--session`s.

## JSON output

Every command supports `--json` and prints a single-line envelope:

```json
{"ok":true,"payload":{"kind":"snapshot","root":{...},"generation":2}}
{"ok":false,"errorCode":"stale-ref","message":"@e4 is not part of the most recent snapshot (generation 2). Run 'agent-windows snapshot' again."}
```

Error codes: `bad-request`, `no-target`, `not-found`, `ambiguous`, `unknown-ref`, `stale-ref`, `timeout`, `elevated-target`, `launch-failed`, `pattern-unsupported`, `internal-error`.

## Windows gotchas

- **Elevation**: a non-elevated process cannot automate an elevated (admin) window. `attach` detects this and fails with `elevated-target` instead of returning an empty tree.
- **Packaged (Store/UWP) apps**: `launch` fails for stub executables; launch the app yourself and `attach --window`.
- **Focus**: `press` needs the window foreground — `window focus` first. Pattern-based actions don't.
- **Daemon locks the exe**: if you built agent-windows from source, `agent-windows daemon stop` before rebuilding.

## Troubleshooting

**`stale-ref`** — the snapshot was superseded. `agent-windows snapshot -i` and use the new refs.

**Element missing from the snapshot** — it may be virtualized, off-screen, or behind a collapsed parent: scroll toward it, `expand` the parent, or drop `-i` / raise `--depth` to see the full tree.

**`pattern-unsupported`** — the element doesn't implement the needed UIA pattern. Fall back to `click` plus `press` (e.g. click a combo box, then arrow keys + Enter), or `click --at x,y` as a last resort.

**`fill` or `press` has no effect** — the window lost focus: `agent-windows window focus`, then retry.

**`timeout` on an action** — the element never became enabled/on-screen. Screenshot to see what's actually there; a modal dialog may be blocking the window.

**Empty or tiny tree after attach** — you may have attached to the wrong window (title substring matched something else). `agent-windows list` and re-attach by `--pid` or `--hwnd`.

## Global flags

```
--json              # machine-readable envelope
--session <name>    # isolated daemon (also AGENT_WINDOWS_SESSION)
--timeout <ms>      # per-action actionability timeout (action commands, default 10000)
```

## Full reference

Deep-dive references live in `references/` next to this file. Read them on demand:

- `references/commands.md` — when you need an exact flag or argument, or hit an error code not explained above
- `references/snapshot-refs.md` — when refs behave unexpectedly, or before using `--scope`/`--depth` on a large app
- `references/repl-batch.md` — before scripting a multi-step sequence through `agent-windows repl`
- `references/windows-gotchas.md` — when actions fail on a specific app (elevation, packaged apps, focus, timing)

No filesystem access to this skill? The CLI serves the same content: `agent-windows skills get --full`.

# agent-windows

Windows-native UI automation CLI for AI agents, built on [Microsoft UI Automation](https://learn.microsoft.com/windows/win32/winauto/entry-uiauto-win32) via [FlaUI](https://github.com/FlaUI/FlaUI). The ergonomics are modeled on [vercel-labs/agent-browser](https://github.com/vercel-labs/agent-browser): take a snapshot, get refs like `@e5` for interactive elements, act on refs.

```
agent-windows attach --window "Notepad"
agent-windows snapshot -i
- window "Untitled - Notepad" [@e21]
  - document "Text editor" focused [@e1]
  - menubar automationId=MenuBar
    - menuitem "File" automationId=File collapsed [@e5]
    ...
agent-windows fill @e1 "hello"
agent-windows click @e5
agent-windows screenshot out.png
```

## How it works

The first command auto-starts a background **daemon** that owns a single UI Automation session: the attached target window and the live element table from the most recent snapshot. The CLI is a thin client that talks to the daemon over a named pipe with newline-delimited JSON. Refs stay valid until the next snapshot; using an old ref returns a `stale-ref` error telling the agent to re-snapshot. Ref ids are never reused across snapshots, so a stale ref can never silently hit the wrong element.

## Commands

| Command | Purpose |
| --- | --- |
| `list` | List top-level windows (title, pid, process, hwnd, elevation) |
| `launch --app <path> [--args <a>]` | Launch an app and attach to its main window |
| `attach --window <title> \| --pid <n> \| --hwnd <n>` | Attach to an existing window (title is substring, case-insensitive) |
| `snapshot [-i] [--depth <n>] [--scope @ref]` | Accessibility tree with refs; `-i` keeps interactive elements only |
| `click [@ref] [--at x,y] [--right] [--middle] [--double]` | Click an element, or coordinates as an escape hatch |
| `fill @ref <text>` | Set the value of an editable element (ValuePattern, keyboard fallback) |
| `press <keys>` | Key or chord: `Enter`, `Ctrl+S`, `Ctrl+Shift+Tab` |
| `select @ref <item>` | Select in combo box, list, or tab control |
| `expand @ref [--collapse]` | Expand/collapse menus, tree items, combo boxes |
| `toggle @ref [--on\|--off]` | Toggle a checkbox; `--on/--off` are idempotent |
| `scroll <direction> [@ref] [--amount n]` | ScrollPattern with mouse-wheel fallback |
| `wait [@ref] [--text <t>] [--gone] [--timeout ms]` | Wait for an element/text to appear or disappear |
| `screenshot <out.png> [--ref @ref]` | Capture target window (or element) as PNG |
| `window <focus\|move\|resize\|maximize\|minimize\|restore>` | Manage the target window |
| `close [--force]` | Close the target window (`--force` kills the process) |
| `status` | Daemon pid, target, snapshot generation, ref count |
| `daemon stop` | Shut down the daemon |

Global options: `--json` (machine-readable envelope), `--session <name>` (parallel isolated daemons; also `AGENT_WINDOWS_SESSION`). Action commands accept `--timeout <ms>` (default 10000) and retry until the element is enabled and on-screen.

## JSON output

Every command supports `--json` and prints a single-line envelope:

```json
{"ok":true,"payload":{"kind":"snapshot","root":{"role":"window","name":"...","ref":"e1","children":[...]},"generation":2}}
{"ok":false,"errorCode":"stale-ref","message":"@e4 is not part of the most recent snapshot (generation 2). Run 'agent-windows snapshot' again."}
```

Error codes: `bad-request`, `no-target`, `not-found`, `unknown-ref`, `stale-ref`, `timeout`, `elevated-target`, `launch-failed`, `pattern-unsupported`, `internal-error`.

## Windows-specific notes

- **Elevation**: a non-elevated process cannot automate an elevated window. `attach` detects this and fails with `elevated-target` instead of returning an empty tree.
- **Packaged (Store/UWP) apps**: `launch` fails for stub executables that hand off to another process (for example `notepad.exe` on Windows 11). Launch the app yourself and use `attach --window <title>`.
- **AutomationId**: shown in snapshots when present — it is the most stable selector Windows apps offer, so agents should prefer elements that have one when disambiguating.
- **Focus**: pattern-based actions (`fill` via ValuePattern, `toggle`, `select`, `expand`) do not require the window to be foreground; keyboard fallbacks and `press` do. Use `window focus` first when sending keys.

## Install / build

Requires Windows and the .NET 10 SDK ([mise](https://mise.jdx.dev) manages it):

```
mise run setup      # install tools, restore, git hooks
mise run build      # analyzer-enforced build (warnings are errors)
mise run test       # all tests
mise run coverage   # tests + HTML coverage report under artifacts/coverage
mise run format     # CSharpier
mise run publish    # self-contained exe + dotnet tool package under artifacts/
```

To install the CLI globally after publishing:

```
dotnet tool install -g AgentWindows --add-source .\artifacts\package
```

The tool package needs the .NET 10 runtime; the self-contained `artifacts\publish\agent-windows.exe` runs without any .NET install. (Internally the tool package is a thin `net10.0` launcher around the `net10.0-windows` CLI, because the SDK's `PackAsTool` rejects windows-specific target frameworks — NETSDK1146.)

## Architecture

- `src/AgentWindows.Core` — cross-platform, dependency-free: protocol records (JSON source-generated), snapshot tree model and text formatter, ref parsing, key-gesture parsing, request dispatcher over `IAutomationSession`. This is where the unit-test coverage lives.
- `src/AgentWindows.Automation` — the only project that touches FlaUI/UIA: tree walking with ref assignment, pattern-based actions with retry/actionability, elevation detection. Verified by integration smoke tests, excluded from unit coverage.
- `src/AgentWindows.Cli` — System.CommandLine front end, daemon host (named-pipe server), daemon client with auto-spawn, output rendering.
- Architecture tests (NetArchTest) enforce the layering: Core never references FlaUI or the other projects.

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
| `skills get [name] [--full]` | Print the bundled agent skill (usage guide); `skills list` enumerates |

Global options: `--json` (machine-readable envelope), `--session <name>` (parallel isolated daemons; also `AGENT_WINDOWS_SESSION`). Action commands accept `--timeout <ms>` (default 10000) and retry until the element is enabled and on-screen.

## REPL / batch mode (fast path for agents)

Each one-shot command pays ~130ms of process startup. `agent-windows repl` starts one process that holds a persistent daemon connection, reads one command per stdin line, and writes exactly one JSON envelope per stdout line (~8ms per command instead of ~200ms):

```
agent-windows repl
snapshot -i
{"ok":true,"payload":{"kind":"snapshot",...},"elapsedMs":50.1}
click @e5
{"ok":true,"payload":{"kind":"ack","detail":"clicked"},"elapsedMs":2.3}
exit
```

Rules: lines are CLI syntax (quotes supported) or a raw JSON request when the line starts with `{`; `#` comments and blank lines are skipped; `exit`/`quit` or stdin EOF end the session (so `agent-windows repl < script.txt` works); the **first failing command ends the session with exit code 1** — agents should re-snapshot and restart the loop. Nothing but response envelopes is ever written to stdout. The session's daemon connection is held for the lifetime of the REPL, so run parallel REPLs on separate `--session`s.

## JSON output

Every command supports `--json` and prints a single-line envelope:

```json
{"ok":true,"payload":{"kind":"snapshot","root":{"role":"window","name":"...","ref":"e1","children":[...]},"generation":2}}
{"ok":false,"errorCode":"stale-ref","message":"@e4 is not part of the most recent snapshot (generation 2). Run 'agent-windows snapshot' again."}
```

Error codes: `bad-request`, `no-target`, `not-found`, `unknown-ref`, `stale-ref`, `timeout`, `elevated-target`, `launch-failed`, `pattern-unsupported`, `internal-error`.

## Agent skill

[skill-data/agent-windows](skill-data/agent-windows/SKILL.md) is a bundled usage guide for AI agents, modeled on agent-browser's skill: the snapshot-and-ref loop, common workflows, Windows gotchas, and troubleshooting, plus reference deep-dives. It is embedded in the CLI, so agents can self-serve:

```
agent-windows skills get          # the skill body
agent-windows skills get --full   # plus references/*.md
```

Install it with the [skills](https://skills.sh) CLI from a local checkout — `npx skills add ./skill-data/agent-windows` (or `<owner>/agent-windows` once published) — or copy `skill-data/agent-windows` into `.claude/skills/` (or `~/.claude/skills/`).

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
mise run test       # unit + architecture tests (e2e tests auto-skip)
mise run e2e        # end-to-end tests: the real CLI drives a bundled WPF target app
mise run coverage   # tests + HTML coverage report under artifacts/coverage
mise run check      # formatting + analyzers + strict Core/CLI coverage gate
mise run format     # CSharpier
mise run bench      # hyperfine latency benchmark against the bundled target app
mise run profile    # dotnet-trace capture of the daemon under snapshot load
mise run publish    # self-contained exe + dotnet tool package under artifacts/
mise run reinstall  # publish, stop all daemons, and replace the global tool
```

To install the CLI globally after publishing:

```
dotnet tool install -g AgentWindows --add-source .\artifacts\package
```

The tool package needs the .NET 10 runtime; the self-contained `artifacts\publish\agent-windows.exe` runs without any .NET install. (Internally the tool package is a thin `net10.0` launcher around the `net10.0-windows` CLI, because the SDK's `PackAsTool` rejects windows-specific target frameworks — NETSDK1146.)

## Architecture

- `src/AgentWindows.Core` — cross-platform, dependency-free, and grouped by capability: `Dispatch`, `Elements`, `Input`, `Protocol`, `Session`, `Snapshots`, and `Windows`. Protocol types are further grouped by `Capture`, `Interaction`, `Lifecycle`, `Transport`, and `Windows`. This is where the unit-test coverage lives.
- `src/AgentWindows.Automation` — the only project that touches FlaUI/UIA, grouped into `Input`, `Session`, `Snapshots`, and `Windows`. Verified by integration smoke tests, excluded from unit coverage.
- `src/AgentWindows.Cli` — System.CommandLine front end grouped into `ConsoleHost` and `Daemon`; the composition root remains in `Program.cs`.
- Test folders mirror their production capability folders. End-to-end test plumbing lives under `Infrastructure`, while user-visible workflows live under `Scenarios`.
- Architecture tests (NetArchTest) enforce the layering: Core never references FlaUI or the other projects.
- The unit-test quality gate requires 100% line, branch, method, and full-method coverage for Core and CLI after a small, architecture-tested allowlist of native composition roots. Automation remains owned by the desktop e2e gate; the dotnet-tool launcher is validated by the publish/reinstall smoke path.
- `tests/AgentWindows.E2eTarget` + `tests/AgentWindows.E2e.Tests` — end-to-end suite: a bundled WPF app with stable AutomationIds, driven by spawning the real CLI (auto-spawned daemon, unique `--session` per test class, assertions on the `--json` envelope). Gated behind `AGENT_WINDOWS_E2E=1` so plain `dotnet test` runs stay headless; run via `mise run e2e` or the CI e2e job.

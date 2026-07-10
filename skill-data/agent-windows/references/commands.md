# Command reference

Every command, argument, and flag. All commands accept the global options; action commands additionally accept `--timeout <ms>`.

## Global options

| Flag | Meaning |
| --- | --- |
| `--json` | Emit the raw single-line JSON response envelope instead of human-readable output. |
| `--session <name>` | Named daemon session. Defaults to `AGENT_WINDOWS_SESSION` or `default`. Each session is a fully isolated daemon with its own target and refs. |

`--timeout <ms>` (default 10000) is accepted by action commands (`launch`, `click`, `fill`, `select`, `expand`, `toggle`, `scroll`, `wait`) and bounds how long the daemon retries until the element is enabled and on-screen.

## Target management

### `list`

List top-level windows on the desktop: title, pid, process name, hwnd, elevation.

### `launch --app <path> [--args <a>] [--timeout <ms>]`

Launch an executable and attach to its main window. Relative paths are resolved against the caller's working directory; bare names (`notepad.exe`) use PATH lookup. Fails with `launch-failed` for packaged-app stub executables — launch those manually and `attach`.

### `attach [--window <title>] [--pid <n>] [--hwnd <n>]`

Attach to an existing window. `--window` matches the first window whose title contains the text (case-insensitive). Fails with `elevated-target` when the window belongs to an elevated process.

### `status`

Daemon pid, attached target, snapshot generation, live ref count.

### `close [--force]`

Close the target window; `--force` kills the owning process instead.

### `daemon stop`

Shut down this session's daemon. The daemon keeps its executable locked while running — stop every session's daemon before replacing or rebuilding the binary.

## Reading

### `snapshot [-i|--interactive] [--depth <n>] [--scope @ref]`

Capture the accessibility tree of the target window with fresh element refs.

- `-i` — only interactive elements (and their ancestors). Preferred: much smaller output.
- `--depth <n>` — maximum tree depth to walk.
- `--scope @ref` — restrict to the subtree of a ref from the previous snapshot.

Taking a snapshot invalidates all refs from earlier snapshots.

### `screenshot <out.png> [--ref @ref]`

Save a PNG of the target window, or of a single element with `--ref`.

## Acting

### `click [@ref] [--at x,y] [--right] [--middle] [--double] [--timeout <ms>]`

Click an element by ref, or screen coordinates with `--at` (escape hatch — prefer refs).

### `fill @ref <text> [--timeout <ms>]`

Replace the value of an editable element. Uses ValuePattern when available (works without focus), keyboard fallback otherwise (needs foreground).

### `press <keys>`

Press a key or chord: `Enter`, `Ctrl+S`, `Ctrl+Shift+Tab`. Sends real keyboard input, so the target window must be foreground (`window focus` first).

### `select @ref <item> [--timeout <ms>]`

Select a named item in a combo box, list, or tab control.

### `expand @ref [--collapse] [--timeout <ms>]`

Expand or collapse a menu item, tree item, or combo box. Menus populate their children on expand — re-snapshot afterwards.

### `toggle @ref [--on|--off] [--timeout <ms>]`

Toggle a checkbox or toggle element. `--on`/`--off` make it idempotent (no-op when already in the requested state).

### `scroll <up|down|left|right> [@ref] [--amount <n>] [--timeout <ms>]`

Scroll an element (or the target window when the ref is omitted). `--amount` is scroll steps (default 3). Uses ScrollPattern with a mouse-wheel fallback.

### `wait [@ref] [--text <t>] [--gone] [--timeout <ms>]`

Wait until an element (by ref) or any element whose name contains `--text` appears — or disappears with `--gone`.

### `window <focus|move|resize|maximize|minimize|restore> [--x <n>] [--y <n>] [--width <n>] [--height <n>]`

Manage the target window. `move` takes `--x`/`--y`; `resize` takes `--width`/`--height`.

## Meta

### `repl`

Read commands from stdin (one per line, CLI syntax or raw JSON) and write one JSON envelope per stdout line. See `references/repl-batch.md`.

### `skills list` / `skills get <name> [--full]`

Print bundled skill documentation. `--full` appends the reference files to the skill body.

## Error codes

| Code | Meaning | Typical fix |
| --- | --- | --- |
| `bad-request` | Malformed arguments or request. | Fix the invocation. |
| `no-target` | No window attached yet. | `attach` or `launch` first. |
| `not-found` | Window/element not found. | Check `list` output or the wait text. |
| `unknown-ref` | Ref was never issued. | Typo — check the snapshot. |
| `stale-ref` | Ref is from a superseded snapshot. | Re-run `snapshot` and use fresh refs. |
| `timeout` | Element never became actionable / condition never held. | Screenshot to inspect; look for a blocking modal. |
| `elevated-target` | Window belongs to an elevated process. | Run agent-windows elevated, or pick another window. |
| `launch-failed` | Process failed to start or show a window. | For packaged apps, launch manually and `attach`. |
| `pattern-unsupported` | Element lacks the required UIA pattern. | Fall back to `click` + `press`. |
| `internal-error` | Unexpected daemon failure. | Retry; `daemon stop` and retry if it persists. |

# Windows gotchas

Desktop automation has failure modes browsers don't. These are the ones that actually bite.

## Elevation (UAC)

A non-elevated process cannot automate an elevated (run-as-administrator) window — Windows blocks it at the UIA level, and naive tools return an empty tree as if the app had no UI. `attach` detects this and fails with `elevated-target` instead.

Options: run `agent-windows` from an elevated shell (then it can automate both elevated and normal windows), or work with a non-elevated instance of the app.

## Packaged (Store/UWP) apps

`launch` starts an executable and waits for *that process* to show a main window. Packaged-app stubs (the `notepad.exe` shim on Windows 11, most Store apps' aliases) immediately hand off to a different process and exit, so `launch` fails with `launch-failed`.

Workaround: start the app any other way — `Start-Process notepad`, the Start menu, `explorer.exe shell:AppsFolder\...` — then `agent-windows attach --window <title>`.

## Focus: pattern actions vs. keyboard

UI Automation has two ways to act on an element:

| Mechanism | Commands | Needs foreground? |
| --- | --- | --- |
| UIA patterns (ValuePattern, TogglePattern, …) | `fill` (usually), `toggle`, `select`, `expand`, `scroll` (usually) | No — works on background windows |
| Real input (keyboard/mouse synthesis) | `press`, `click`, fallbacks when a pattern is missing | Yes |

So a background window can often be filled and toggled without stealing focus from the user — but the moment you `press` keys, run `agent-windows window focus` first or the keystrokes land in whatever window *is* focused.

## AutomationId is the stable selector

Names are localized, change with content ("3 unread messages"), and get duplicated. `automationId` is set by the app developer and is stable across runs, languages, and layouts. When a snapshot shows several similar elements, act on the one with an `automationId` — and when scripting a repeated flow, note the AutomationIds so a re-snapshot can re-find the same controls by identity rather than position.

Not every app sets them: Win32 apps often expose control ids, WPF/WinUI apps expose them when developers bother, Electron/Chromium apps mostly don't (their tree comes from the DOM).

## Timing

- Action commands already retry until the element is enabled and on-screen (`--timeout`, default 10000ms) — don't add sleeps before actions.
- Windows apps often show a window before its content is ready. After `launch` or `attach`, if the snapshot looks empty, `wait --text` on something you expect, then re-snapshot.
- Modal dialogs steal all interaction from their owner window: if actions start timing out, screenshot — there's probably a dialog waiting.

## Daemon lifecycle

- The daemon auto-starts on first use and keeps the exe file locked. When building agent-windows from source, run `agent-windows daemon stop` (for every named session you used) before rebuilding, or the build fails with a file-in-use error.
- `status` shows the daemon pid, target, snapshot generation, and ref count — cheap sanity check when behavior seems off.
- The daemon holds one target window at a time per session. Attaching to a new window drops the old one; use separate `--session`s to hold several.

## Screenshots and coordinates

`screenshot` captures the target window's bounds — including whatever overlaps it if the window is in the background. `window focus` (or `window restore` for minimized windows) before capturing. Coordinates for `click --at` are **screen** coordinates, matching the values in `window move`/`resize`.

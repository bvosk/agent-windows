# ABT Maint Automation Punchlist

This punchlist captures improvements identified while using `agent-windows` to log in to ABT Maint and open work order `1856581`. Items are ordered by expected impact.

## Punchlist

- [ ] **Fix untitled and modal window discovery**
  - Include visible top-level windows whose titles are empty.
  - Make `attach --pid` prefer enabled, non-zero-size modal or owned windows over hidden or disabled parent windows.
  - Provide a useful fallback name for untitled windows in command output.

- [ ] **Automatically follow same-process window transitions**
  - Retarget when the current window closes or becomes disabled and a new actionable window appears in the same process.
  - Cover transitions from login to the main application, Work Order Listing, and View Work Order.

- [ ] **Add selector-based actions**
  - Support actions by automation ID, role, accessible name, and combinations of those properties.
  - Avoid requiring a full snapshot and transient element ref for every action.
  - Detect ambiguous selectors and return the matching candidates.

- [ ] **Add legacy Delphi grid support**
  - Use UIA Raw View or an MSAA fallback to expose grid rows and cell values.
  - Allow selecting, invoking, or double-clicking a row based on a cell value such as `WO # = 1856581`.
  - Eliminate screenshot inspection and coordinate clicks for result rows.

- [ ] **Prefer UI Automation patterns over physical mouse input**
  - Use Invoke, Selection, Toggle, and other appropriate patterns before moving the cursor.
  - Keep physical mouse input as a compatibility fallback.

- [ ] **Add window-aware waits**
  - Support waiting for a window by title, process, owner, or selector.
  - Allow attach operations to wait for a matching window.
  - Replace fixed sleeps and repeated window-list polling.

- [ ] **Associate labels with unlabeled controls**
  - Infer nearby static-text labels for edit controls that lack accessible names.
  - Include the inferred label in interactive snapshots and selector matching.
  - Cover fields such as User Name, Password, and Work Order #.

- [ ] **Add stronger fallback text entry**
  - Add a `type <text>` command for the focused control.
  - Consider `fill --focused` and coordinate-targeted filling.
  - Avoid sending multi-character values one key command at a time.

- [ ] **Make window listing sessionless**
  - Execute `list` in-process without spawning an automation daemon.
  - Avoid creating an unused `default` daemon while another named session is active.

- [ ] **Add compact query and snapshot modes**
  - Provide server-side element search and scoped queries.
  - Consider differential or cached snapshots for repeated inspection.
  - Reduce full-tree traversals and duplicated menu subtrees.

- [ ] **Improve window diagnostics**
  - Add a mode such as `list --all --pid <pid>`.
  - Show untitled, hidden, disabled, owned, and zero-size windows.
  - Include handles, bounds, owner relationships, and actionability information.

- [ ] **Add regression coverage for legacy applications**
  - Test untitled owned dialogs and hidden bootstrap windows.
  - Test automatic target replacement within one process.
  - Test unlabeled controls and legacy-accessibility grids.

## Confirmed Working

- [x] Cursor movement is instantaneous after replacing FlaUI's animated `Mouse.MoveTo` behavior with direct `Mouse.Position` assignment.
- [x] `mise run reinstall` republishes the project, stops all daemon sessions, replaces the global tool, and verifies the installed build.

# Snapshots and refs

## The model

`snapshot` walks the target window's UI Automation tree and prints one line per element:

```
- window "Untitled - Notepad" [@e21]
  - document "Text editor" focused [@e1]
  - menubar automationId=MenuBar
    - menuitem "File" automationId=File collapsed [@e5]
```

Each line shows the control role, the element's name in quotes, the `automationId` when the app defines one, state tokens (`disabled`, `focused`, `offscreen`, `checked`/`unchecked`, `expanded`/`collapsed`, `selected`), the current `value="..."` for value-bearing elements, and — for elements you can act on — a ref like `[@e5]`.

The daemon keeps a table mapping each ref to the live UIA element, so acting on `@e5` is a direct lookup, not a re-search. That table belongs to the **most recent snapshot only**.

## Staleness and generations

Every snapshot increments a generation counter (visible in `status` and the JSON payload) and assigns fresh refs. Ref ids are **never reused across snapshots**, so a ref from an old generation can never silently resolve to the wrong element — it fails fast:

```json
{"ok":false,"errorCode":"stale-ref","message":"@e4 is not part of the most recent snapshot (generation 2). Run 'agent-windows snapshot' again."}
```

Practical rules:

- Re-snapshot after any action that changes the UI: expanding a menu, a dialog opening or closing, content loading.
- Between a snapshot and an action on its refs, do nothing that could snapshot again (including in another terminal on the same session).
- `unknown-ref` means the ref was never issued (typo); `stale-ref` means it was issued by an older snapshot.

Unlike a browser, the ref table does not invalidate itself when the app repaints — refs point at live UIA elements and often keep working across minor UI changes. Staleness is only enforced across snapshots. But an element that was destroyed and recreated by the app will fail actionability; when in doubt, re-snapshot.

## Interactive filter (`-i`)

`-i` keeps only elements an agent can act on (buttons, edits, menu items, checkboxes, list items, …) plus the ancestors needed to show their context. This is the default choice — full trees of real apps are large. Drop `-i` when you need static text, labels, or structure that the filter hides.

## Scoping and depth

- `--scope @ref` re-walks only the subtree under a ref from the **previous** snapshot — useful for drilling into one pane of a big window. The scoped snapshot is still a new generation: all other refs go stale.
- `--depth <n>` bounds the walk; combine with `--scope` to expand the tree incrementally in big apps.

## Choosing elements

1. Prefer elements with an `automationId` — it is assigned by the app developer and survives restarts, localization, and layout changes.
2. Otherwise match on role + name.
3. `click --at x,y` is the last resort for elements UIA cannot see (custom-rendered canvases, some games). Take a `screenshot` first to read coordinates.

## JSON shape

```json
{"ok":true,"payload":{"kind":"snapshot","root":{"role":"window","name":"...","ref":"e1","children":[...]},"generation":2}}
```

Refs in JSON omit the `@` prefix; commands accept them with it.

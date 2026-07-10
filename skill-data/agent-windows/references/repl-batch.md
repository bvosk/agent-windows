# REPL / batch mode

Every one-shot `agent-windows` invocation is a new process: ~130ms of startup before the daemon even sees the request. The REPL amortizes that to nothing — one process, one persistent daemon connection, ~8ms per command instead of ~200ms. Any sequence longer than 2–3 commands should run through it.

## Protocol

```
agent-windows repl
```

- **Input**: one command per stdin line, in normal CLI syntax. Quoting works: `fill @e1 "hello world"`.
- **Raw JSON**: a line starting with `{` is sent to the daemon as a raw protocol request, bypassing CLI parsing.
- **Output**: exactly one single-line JSON envelope per command, and nothing else, ever, on stdout. Each envelope carries `elapsedMs`.
- `#` comments and blank lines are skipped.
- `exit`, `quit`, or stdin EOF end the session.
- Global flags (`--session`) go on the `repl` invocation itself, not on individual lines.

## Failure semantics

The **first failing command ends the session with exit code 1**. This is deliberate: later commands in a script almost always depend on earlier ones, and refs from before a failure are suspect. On failure: re-snapshot, rebuild the plan, start a new REPL.

## Batch scripts

EOF ends the session, so scripts pipe straight in:

```
agent-windows repl < script.txt
```

```
# script.txt — save a file in Notepad
snapshot -i
press Ctrl+S
wait --text "Save As"
snapshot -i
```

Note the limitation of a pre-written batch: later commands can't reference refs discovered by earlier snapshots in the same batch (you don't know the ids ahead of time). Batches work best for ref-free sequences (`press`, `wait --text`, `window focus`, re-snapshots); for ref-dependent flows, drive the REPL interactively — read the envelope, then write the next line.

## Driving the REPL interactively from an agent

Keep the process open and speak line-per-line. For example, from a shell with a coprocess, or simplest: run short REPL bursts where each burst starts with `snapshot -i` and uses the refs it just learned:

```
agent-windows repl <<'EOF'
snapshot -i
EOF
# ...read refs from the envelope, then:
agent-windows repl <<'EOF'
click @e5
wait --text "Open"
snapshot -i
EOF
```

Refs survive across REPL sessions — they belong to the daemon's latest snapshot, not to the REPL process.

## Parallel sessions

One REPL holds its session's daemon connection for its whole lifetime. To drive two apps concurrently, use separate sessions:

```
agent-windows --session a repl < script-a.txt
agent-windows --session b repl < script-b.txt
```

Each named session is a fully isolated daemon: own target window, own snapshot generation, own refs. Set `AGENT_WINDOWS_SESSION` to avoid repeating the flag. Remember to `daemon stop` each session when done.

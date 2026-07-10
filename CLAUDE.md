# agent-windows

Windows-native UI automation CLI for AI agents (FlaUI/UIA under the hood, agent-browser-style UX). See README.md for the command surface and architecture.

## Working in this repo

- Use mise tasks: `mise run build`, `mise run test`, `mise run coverage`, `mise run check`, `mise run format`. No dedicated dev CLI.
- Warnings are errors with `AnalysisLevel=latest-All` plus StyleCop, Roslynator, Sonar, Meziantou, and BannedApiAnalyzers. Expect strict style rules (expression bodies, `var` everywhere, one type per file, `_camelCase` private fields including consts, static members before instance members).
- CSharpier owns formatting; run `dotnet csharpier format .` before committing (Husky pre-commit does this for staged files).
- Layering is enforced by architecture tests: `AgentWindows.Core` must stay free of FlaUI and Windows-only dependencies. New automation behavior goes behind `IAutomationSession`; pure logic (parsing, formatting, protocol) goes in Core with unit tests.
- The wire protocol (`ProtocolSerializer`, request/payload records) is a compatibility surface — tests pin the JSON shape. Change it deliberately.
- `AgentWindows.Automation` is excluded from unit coverage (needs a live desktop). Validate changes there with `mise run e2e` — the e2e suite spawns the real CLI against the bundled WPF target app (tests/AgentWindows.E2eTarget). E2e tests are gated behind `AGENT_WINDOWS_E2E=1` and auto-skip in plain `dotnet test`. They pop real windows and grab focus, so don't run them while the user is typing.
- Remember `agent-windows daemon stop` after manual CLI experiments — a running daemon locks the built exe and makes rebuilds fail.

# agent-windows

Windows-native UI automation CLI for AI agents (FlaUI/UIA under the hood, agent-browser-style UX). See README.md for the command surface and architecture.

## Working in this repo

- Use mise tasks: `mise run build`, `mise run test`, `mise run coverage`, `mise run format`. No dedicated dev CLI.
- Warnings are errors with `AnalysisLevel=latest-All` plus StyleCop, Roslynator, Sonar, Meziantou, and BannedApiAnalyzers. Expect strict style rules (expression bodies, `var` everywhere, one type per file, `_camelCase` private fields including consts, static members before instance members).
- CSharpier owns formatting; run `dotnet csharpier format .` before committing (Husky pre-commit does this for staged files).
- Layering is enforced by architecture tests: `AgentWindows.Core` must stay free of FlaUI and Windows-only dependencies. New automation behavior goes behind `IAutomationSession`; pure logic (parsing, formatting, protocol) goes in Core with unit tests.
- The wire protocol (`ProtocolSerializer`, request/payload records) is a compatibility surface — tests pin the JSON shape. Change it deliberately.
- `AgentWindows.Automation` is excluded from unit coverage (needs a live desktop). Validate changes there by driving a real app: build, then `agent-windows attach --window Notepad`, `snapshot -i`, act, and `daemon stop` when done (the daemon locks the built exe and will make rebuilds fail while running).

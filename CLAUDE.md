# agent-windows

Windows-native UI automation CLI for AI agents (FlaUI/UIA under the hood, agent-browser-style UX). See README.md for the command surface and architecture.

## Working in this repo

- Use mise tasks: `mise run build`, `mise run test`, `mise run coverage`, `mise run check`, `mise run check:full`, `mise run format`. No dedicated dev CLI.
- Warnings are errors with `AnalysisLevel=10.0-All` plus StyleCop, Roslynator, Sonar, Meziantou, and BannedApiAnalyzers. Expect strict style rules (expression bodies, `var` everywhere, one type per file, `_camelCase` private fields including consts, static members before instance members).
- CSharpier owns formatting; run `dotnet csharpier format .` before committing (Husky pre-commit does this for staged files).
- Layering is enforced by architecture tests: `AgentWindows.Core` must stay free of FlaUI and Windows-only dependencies. New automation behavior goes behind `IAutomationSession`; pure logic (parsing, formatting, protocol) goes in Core with unit tests.
- Organize source by capability within each project and keep namespaces aligned with folders. Mirror production capability folders in unit tests; keep e2e harness code in `Infrastructure` and workflows in `Scenarios`.
- The wire protocol (`ProtocolSerializer`, request/payload records) is a compatibility surface — tests pin the JSON shape. Change it deliberately.
- `AgentWindows.Automation` is included in the repository coverage baseline but primarily validated with `mise run e2e`, which spawns the real CLI against the bundled WPF target app. E2E tests are gated behind `AGENT_WINDOWS_E2E=1`, pop real windows, and grab focus, so don't run them while the user is typing.
- `mise run coverage` enforces the 50% aggregate line-coverage baseline across Core, CLI, and Automation. Native composition exclusions are governed by `CoverageExclusionTests`; do not add or broaden one without documenting its alternate validation path there.
- Remember `agent-windows daemon stop` after manual CLI experiments — a running daemon locks the built exe and makes rebuilds fail.

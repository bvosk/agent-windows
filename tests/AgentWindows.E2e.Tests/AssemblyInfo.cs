using Xunit;

// UIA drives global desktop state (focus, cursor, keyboard); never run in parallel.
[assembly: AssemblyFixture(typeof(AgentWindows.E2e.Tests.DesktopAutomationLock))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

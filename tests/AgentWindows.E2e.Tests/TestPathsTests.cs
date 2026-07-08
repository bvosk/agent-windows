using Shouldly;
using Xunit;

namespace AgentWindows.E2e.Tests;

/// <summary>
/// Always runs (no desktop needed) so the assembly executes at least one test when
/// the e2e gate is off; Microsoft.Testing.Platform fails assemblies that run zero.
/// </summary>
public sealed class TestPathsTests
{
    [Fact]
    public void CliExecutablePath_ResolvesUnderTheRepo() =>
        TestPaths.CliExecutable.ShouldEndWith("agent-windows.exe");

    [Fact]
    public void TargetAppExecutablePath_ResolvesUnderTheRepo() =>
        TestPaths.TargetAppExecutable.ShouldEndWith("AgentWindows.E2eTarget.exe");
}

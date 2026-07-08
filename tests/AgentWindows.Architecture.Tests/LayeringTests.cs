using AgentWindows.Automation;
using AgentWindows.Core.Model;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AgentWindows.Architecture.Tests;

public sealed class LayeringTests
{
    [Fact]
    public void Core_DoesNotDependOnFlaUi()
    {
        var result = Types
            .InAssembly(typeof(UiNode).Assembly)
            .ShouldNot()
            .HaveDependencyOn("FlaUI")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Core_DoesNotDependOnAutomationOrCli()
    {
        var result = Types
            .InAssembly(typeof(UiNode).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("AgentWindows.Automation", "AgentWindows.Cli")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Automation_DoesNotDependOnCli()
    {
        var result = Types
            .InAssembly(typeof(FlaUiSession).Assembly)
            .ShouldNot()
            .HaveDependencyOn("AgentWindows.Cli")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void CoreTypes_LiveInCoreNamespace()
    {
        var result = Types
            .InAssembly(typeof(UiNode).Assembly)
            .That()
            .ArePublic()
            .Should()
            .ResideInNamespaceStartingWith("AgentWindows.Core")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    private static string FailingTypes(NetArchTest.Rules.TestResult result) =>
        result.IsSuccessful
            ? ""
            : "Failing types: " + string.Join(", ", result.FailingTypeNames ?? []);
}

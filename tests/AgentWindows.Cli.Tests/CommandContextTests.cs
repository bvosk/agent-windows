using System.CommandLine;
using AgentWindows.Core.Protocol;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests;

public sealed class CommandContextTests
{
    [Fact]
    public void GetSession_ReturnsParsedValue()
    {
        var root = CommandTree.Build(out var context);
        var result = root.Parse("list --session custom", CommandTree.CreateConfiguration());

        context.GetSession(result).ShouldBe("custom");
    }

    [Fact]
    public void GetSession_WithoutOptionValue_ReturnsDefaultSession()
    {
        var jsonOption = new Option<bool>("--json");
        var sessionOption = new Option<string>("--session");
        var context = new CommandContext(jsonOption, sessionOption);
        var root = new RootCommand();
        root.Options.Add(sessionOption);
        var result = root.Parse("");

        context.GetSession(result).ShouldBe(PipeNames.DefaultSession);
    }
}

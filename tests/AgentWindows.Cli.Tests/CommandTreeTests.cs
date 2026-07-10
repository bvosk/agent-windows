using System.CommandLine;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests;

public sealed class CommandTreeTests
{
    private readonly RootCommand _root = CommandTree.Build();

    [Theory]
    [InlineData("list")]
    [InlineData("launch --app notepad.exe")]
    [InlineData("launch --app notepad.exe --args file.txt --timeout 5000")]
    [InlineData("attach --window Calculator")]
    [InlineData("attach --pid 1234")]
    [InlineData("attach --hwnd 65538")]
    [InlineData("snapshot")]
    [InlineData("snapshot -i --depth 10 --scope @e3")]
    [InlineData("click @e4")]
    [InlineData("click --at 640,220 --right --double")]
    [InlineData("fill @e7 hello")]
    [InlineData("press Ctrl+S")]
    [InlineData("select @e2 Red")]
    [InlineData("expand @e5 --collapse")]
    [InlineData("toggle @e6 --on")]
    [InlineData("scroll down @e8 --amount 5")]
    [InlineData("scroll down")]
    [InlineData("wait @e3 --gone")]
    [InlineData("wait --text Saved --timeout 3000")]
    [InlineData("screenshot out.png --ref @e1")]
    [InlineData("window maximize")]
    [InlineData("window move --x 0 --y 0")]
    [InlineData("close --force")]
    [InlineData("status")]
    [InlineData("daemon stop")]
    [InlineData("skills list")]
    [InlineData("skills get")]
    [InlineData("skills get agent-windows")]
    [InlineData("skills get agent-windows --full")]
    [InlineData("skills get --full")]
    [InlineData("list --json --session other")]
    public void Parse_AcceptsDocumentedCommandLines(string commandLine)
    {
        var result = _root.Parse(commandLine, CommandTree.CreateConfiguration());

        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("launch")]
    [InlineData("fill @e7")]
    [InlineData("press")]
    public void Parse_RejectsInvalidCommandLines(string commandLine)
    {
        var result = _root.Parse(commandLine, CommandTree.CreateConfiguration());

        result.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void AllActionCommandsAreRegistered()
    {
        var names = _root.Subcommands.Select(c => c.Name).ToArray();

        names.ShouldBe([
            "list",
            "launch",
            "attach",
            "snapshot",
            "click",
            "fill",
            "press",
            "select",
            "expand",
            "toggle",
            "scroll",
            "wait",
            "screenshot",
            "window",
            "close",
            "status",
            "daemon",
            "repl",
            "skills",
        ]);
    }
}

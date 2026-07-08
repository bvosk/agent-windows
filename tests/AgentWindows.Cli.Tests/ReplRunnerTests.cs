using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests;

public sealed class ReplRunnerTests
{
    private readonly List<DaemonRequest> _sent = [];
    private readonly ReplRunner _runner;
    private Func<DaemonRequest, DaemonResponse> _respond = _ =>
        DaemonResponse.Success(new AckPayload { Detail = "done" });

    public ReplRunnerTests()
    {
        var root = CommandTree.Build(out var context);
        _runner = new ReplRunner(
            root,
            context,
            request =>
            {
                _sent.Add(request);
                return _respond(request);
            }
        );
    }

    [Fact]
    public void CliLine_IsTranslatedAndAnswered()
    {
        var (exitCode, lines) = Run("press Ctrl+S\n");

        exitCode.ShouldBe(0);
        _sent.ShouldHaveSingleItem().ShouldBeOfType<PressRequest>().Keys.ShouldBe("Ctrl+S");
        lines.ShouldHaveSingleItem().ShouldContain("\"ok\":true");
    }

    [Fact]
    public void QuotedArguments_SurviveTokenization()
    {
        var (exitCode, _) = Run("fill @e7 \"hello world\"\n");

        exitCode.ShouldBe(0);
        var fill = _sent.ShouldHaveSingleItem().ShouldBeOfType<FillRequest>();
        fill.Ref.ShouldBe("@e7");
        fill.Text.ShouldBe("hello world");
    }

    [Fact]
    public void JsonLine_IsForwardedAsARequest()
    {
        var (exitCode, _) = Run("""{"cmd":"click","ref":"e5"}""" + "\n");

        exitCode.ShouldBe(0);
        _sent.ShouldHaveSingleItem().ShouldBeOfType<ClickRequest>().Ref.ShouldBe("e5");
    }

    [Fact]
    public void CommentsAndBlankLines_AreSkippedSilently()
    {
        var (exitCode, lines) = Run("# warmup\n\n   \nstatus\n");

        exitCode.ShouldBe(0);
        _sent.ShouldHaveSingleItem().ShouldBeOfType<StatusRequest>();
        lines.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("quit")]
    public void ExitCommands_EndTheSessionWithoutOutput(string command)
    {
        var (exitCode, lines) = Run($"{command}\nstatus\n");

        exitCode.ShouldBe(0);
        _sent.ShouldBeEmpty();
        lines.ShouldBeEmpty();
    }

    [Fact]
    public void UnparseableCliLine_FailsFastWithBadRequest()
    {
        var (exitCode, lines) = Run("frobnicate\nstatus\n");

        exitCode.ShouldBe(1);
        _sent.ShouldBeEmpty();
        lines.ShouldHaveSingleItem().ShouldContain(ErrorCodes.BadRequest);
    }

    [Fact]
    public void UnparseableJsonLine_FailsFastWithBadRequest()
    {
        var (exitCode, lines) = Run("{not json}\n");

        exitCode.ShouldBe(1);
        lines.ShouldHaveSingleItem().ShouldContain(ErrorCodes.BadRequest);
    }

    [Fact]
    public void FailedResponse_EndsTheSessionAndSkipsRemainingLines()
    {
        _respond = _ => DaemonResponse.Failure(ErrorCodes.StaleRef, "take a new snapshot");

        var (exitCode, lines) = Run("status\nstatus\n");

        exitCode.ShouldBe(1);
        _sent.ShouldHaveSingleItem();
        lines.ShouldHaveSingleItem().ShouldContain(ErrorCodes.StaleRef);
    }

    [Fact]
    public void NestedReplAndDaemonRun_AreRejected()
    {
        var (exitCode, lines) = Run("repl\n");

        exitCode.ShouldBe(1);
        _sent.ShouldBeEmpty();
        lines.ShouldHaveSingleItem().ShouldContain(ErrorCodes.BadRequest);
    }

    [Fact]
    public void EndOfInput_ExitsCleanly()
    {
        var (exitCode, lines) = Run("");

        exitCode.ShouldBe(0);
        lines.ShouldBeEmpty();
    }

    private (int ExitCode, IReadOnlyList<string> Lines) Run(string input)
    {
        using var reader = new StringReader(input);
        using var writer = new StringWriter();
        var exitCode = _runner.Run(reader, writer);
        var lines = writer
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
        return (exitCode, lines);
    }
}

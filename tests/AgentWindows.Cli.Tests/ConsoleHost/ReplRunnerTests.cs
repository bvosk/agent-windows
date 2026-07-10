using AgentWindows.Cli.ConsoleHost;
using AgentWindows.Core.Protocol.Interaction;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests.ConsoleHost;

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
            "default",
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
    public void ForeignSession_IsRejected()
    {
        var (exitCode, lines) = Run("status --session other\n");

        exitCode.ShouldBe(1);
        _sent.ShouldBeEmpty();
        lines.ShouldHaveSingleItem().ShouldContain(ErrorCodes.BadRequest);
    }

    [Fact]
    public void ExplicitOwnSession_IsAllowed()
    {
        var (exitCode, _) = Run("status --session default\n");

        exitCode.ShouldBe(0);
        _sent.ShouldHaveSingleItem().ShouldBeOfType<StatusRequest>();
    }

    [Fact]
    public void EndOfInput_ExitsCleanly()
    {
        var (exitCode, lines) = Run("");

        exitCode.ShouldBe(0);
        lines.ShouldBeEmpty();
    }

    [Fact]
    public void RawSender_ResponseIsPassedThroughWithoutReserialization()
    {
        var root = CommandTree.Build(out var context);
        var raw = "{\"ok\":true,\"payload\":{\"type\":\"ack\",\"detail\":\"raw\"}}";
        var runner = new ReplRunner(
            root,
            context,
            "default",
            _ => throw new InvalidOperationException("The typed sender must not be used."),
            _ => raw
        );
        using var reader = new StringReader("status\n");
        using var writer = new StringWriter();

        runner.Run(reader, writer).ShouldBe(0);
        writer.ToString().Trim().ShouldBe(raw);
    }

    [Fact]
    public void RawSender_AcceptsRawJsonRequests()
    {
        var root = CommandTree.Build(out var context);
        DaemonRequest? sent = null;
        var runner = new ReplRunner(
            root,
            context,
            "default",
            _ => throw new InvalidOperationException(),
            request =>
            {
                sent = request;
                return "{\"ok\":true}";
            }
        );
        using var reader = new StringReader("{\"cmd\":\"status\"}\n");
        using var writer = new StringWriter();

        runner.Run(reader, writer).ShouldBe(0);
        sent.ShouldBeOfType<StatusRequest>();
    }

    [Fact]
    public void RawSender_FailureStopsTheSession()
    {
        var root = CommandTree.Build(out var context);
        var sent = 0;
        var runner = new ReplRunner(
            root,
            context,
            "default",
            _ => throw new InvalidOperationException("The typed sender must not be used."),
            _ =>
            {
                sent++;
                return "{\"ok\":false,\"errorCode\":\"timeout\",\"message\":\"slow\"}";
            }
        );
        using var reader = new StringReader("status\nstatus\n");
        using var writer = new StringWriter();

        runner.Run(reader, writer).ShouldBe(1);
        sent.ShouldBe(1);
    }

    [Fact]
    public void RawSender_ResponseWithoutOkIsAFailure()
    {
        var root = CommandTree.Build(out var context);
        var runner = new ReplRunner(
            root,
            context,
            "default",
            _ => throw new InvalidOperationException(),
            _ => "{}"
        );
        using var reader = new StringReader("status\n");
        using var writer = new StringWriter();

        runner.Run(reader, writer).ShouldBe(1);
    }

    [Fact]
    public void RawSender_StillSerializesLocalValidationFailures()
    {
        var root = CommandTree.Build(out var context);
        var runner = new ReplRunner(
            root,
            context,
            "default",
            _ => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException("Invalid input must not be sent.")
        );
        using var reader = new StringReader("frobnicate\n");
        using var writer = new StringWriter();

        runner.Run(reader, writer).ShouldBe(1);
        writer.ToString().ShouldContain(ErrorCodes.BadRequest);
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

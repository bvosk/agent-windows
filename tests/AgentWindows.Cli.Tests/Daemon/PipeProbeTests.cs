using System.IO.Pipes;
using AgentWindows.Cli.Daemon;
using AgentWindows.Core.Protocol.Transport;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests.Daemon;

public sealed class PipeProbeTests
{
    [Fact]
    public void ListSessions_FiltersDeduplicatesAndSortsPipeNames()
    {
        var sessions = PipeProbe.ListSessions(
            (directory, pattern) =>
            {
                directory.ShouldBe(@"\\.\pipe\");
                pattern.ShouldBe($"{PipeNames.Prefix}*");
                return
                [
                    null!,
                    @"\\.\pipe\unrelated",
                    $@"\\.\pipe\{PipeNames.Prefix}",
                    $@"\\.\pipe\{PipeNames.Prefix}zeta",
                    $@"\\.\pipe\{PipeNames.Prefix}alpha",
                    $@"\\.\pipe\{PipeNames.Prefix}alpha",
                ];
            }
        );

        sessions.ShouldBe(["alpha", "zeta"]);
    }

    [Fact]
    public void Exists_AvailablePipeReturnsTrueWithoutReadingLastError()
    {
        var exists = PipeProbe.Exists(
            "agent-windows-test",
            (path, timeout) =>
            {
                path.ShouldBe(@"\\.\pipe\agent-windows-test");
                timeout.ShouldBe(0);
                return true;
            },
            static () => throw new InvalidOperationException("Last error should not be read.")
        );

        exists.ShouldBeTrue();
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(231, true)]
    public void Exists_DistinguishesMissingFromBusyPipe(int error, bool expected)
    {
        var exists = PipeProbe.Exists("agent-windows-test", static (_, _) => false, () => error);

        exists.ShouldBe(expected);
    }

    [Fact]
    public void NativeProbe_DetectsMissingAndAvailablePipes()
    {
        var missing = $"agent-windows-test-missing-{Guid.NewGuid():N}";
        PipeProbe.Exists(missing).ShouldBeFalse();

        var available = $"agent-windows-test-live-{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            available,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1
        );

        PipeProbe.Exists(available).ShouldBeTrue();
    }

    [Fact]
    public void ListSessions_FindsLiveNamedPipe()
    {
        var session = $"test-list-{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            PipeNames.For(session),
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1
        );

        PipeProbe.ListSessions().ShouldContain(session);
        PipeProbe.ListSessions().ShouldContain(session);
    }
}

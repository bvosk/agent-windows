using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests;

public sealed class DaemonManagerTests
{
    [Fact]
    public void StopAll_NoSessionsReportsNothingRunning()
    {
        var response = DaemonManager.StopAll(
            static () => [],
            _ => throw new InvalidOperationException("No pipe should be probed."),
            _ => throw new InvalidOperationException("No session should be stopped."),
            static () => 7,
            static _ => TimeSpan.Zero,
            _ => throw new InvalidOperationException("No delay should occur.")
        );

        response.Ok.ShouldBeTrue();
        response.Payload.ShouldBeOfType<AckPayload>().Detail.ShouldBe("no daemons running");
    }

    [Fact]
    public void StopAll_MultipleSessionsStopsEachAndReportsNames()
    {
        var stopped = new List<string>();
        var probes = new List<string>();

        var response = DaemonManager.StopAll(
            static () => ["alpha", "beta"],
            pipeName =>
            {
                probes.Add(pipeName);
                return false;
            },
            session =>
            {
                stopped.Add(session);
                return Success();
            },
            static () => 7,
            static _ => TimeSpan.Zero,
            _ => throw new InvalidOperationException("No delay should occur.")
        );

        response.Ok.ShouldBeTrue();
        response
            .Payload.ShouldBeOfType<AckPayload>()
            .Detail.ShouldBe("stopped 2 daemon(s): alpha, beta");
        stopped.ShouldBe(["alpha", "beta"]);
        probes.ShouldBe([PipeNames.For("alpha"), PipeNames.For("beta")]);
    }

    [Fact]
    public void StopAll_FirstFailureStopsProcessingAndReturnsFailure()
    {
        var failure = DaemonResponse.Failure(ErrorCodes.InternalError, "could not stop beta");
        var stopped = new List<string>();

        var response = DaemonManager.StopAll(
            static () => ["alpha", "beta", "gamma"],
            _ => throw new InvalidOperationException("Pipes should not be polled after failure."),
            session =>
            {
                stopped.Add(session);
                return string.Equals(session, "beta", StringComparison.Ordinal)
                    ? failure
                    : Success();
            },
            static () =>
                throw new InvalidOperationException("Timer should not start after failure."),
            static _ => TimeSpan.Zero,
            _ => throw new InvalidOperationException("No delay should occur.")
        );

        response.ShouldBeSameAs(failure);
        stopped.ShouldBe(["alpha", "beta"]);
    }

    [Fact]
    public void StopAll_WaitsUntilPipeDisappears()
    {
        var exists = new Queue<bool>([true, false]);
        var delays = new List<TimeSpan>();

        var response = DaemonManager.StopAll(
            static () => ["alpha"],
            pipeName =>
            {
                pipeName.ShouldBe(PipeNames.For("alpha"));
                return exists.Dequeue();
            },
            _ => Success(),
            static () => 37,
            started =>
            {
                started.ShouldBe(37);
                return TimeSpan.FromSeconds(9);
            },
            delays.Add
        );

        response.Ok.ShouldBeTrue();
        delays.ShouldBe([TimeSpan.FromMilliseconds(25)]);
    }

    [Fact]
    public void StopAll_TimeoutReportsBusySession()
    {
        var exception = Should.Throw<AutomationException>(() =>
            DaemonManager.StopAll(
                static () => ["alpha"],
                _ => true,
                _ => Success(),
                static () => 11,
                started =>
                {
                    started.ShouldBe(11);
                    return TimeSpan.FromSeconds(10);
                },
                _ => throw new InvalidOperationException("No delay should occur after timeout.")
            )
        );

        exception.Code.ShouldBe(ErrorCodes.InternalError);
        exception.Message.ShouldContain("Timed out");
        exception.Message.ShouldContain("busy");
    }

    private static DaemonResponse Success() =>
        DaemonResponse.Success(new AckPayload { Detail = "stopped" });
}

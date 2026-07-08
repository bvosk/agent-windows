using System.Diagnostics;
using AgentWindows.Core.Protocol;
using Shouldly;
using Xunit;

namespace AgentWindows.E2e.Tests;

public sealed class DaemonLifecycleTests(TargetAppFixture fixture) : IClassFixture<TargetAppFixture>
{
    private readonly TargetAppFixture _fixture = fixture;

    [E2EFact]
    public async Task FirstCommand_AutoSpawnsADaemon_AndStopTerminatesIt()
    {
        var runner = new CliRunner($"e2e-{Guid.NewGuid():N}");

        var status = await runner.RunAsync("status");
        var daemonPid = status.ShouldSucceedWith<StatusPayload>().Status.DaemonProcessId;
        daemonPid.ShouldBeGreaterThan(0);

        (await runner.RunAsync("daemon", "stop")).ShouldSucceed();

        WaitForProcessExit(daemonPid);
    }

    [E2EFact]
    public async Task Sessions_AreIsolatedFromEachOther()
    {
        var other = new CliRunner($"e2e-{Guid.NewGuid():N}");
        try
        {
            var fixtureStatus = await _fixture.Cli.RunAsync("status");
            fixtureStatus.ShouldSucceedWith<StatusPayload>().Status.Target.ShouldNotBeNull();

            var otherStatus = await other.RunAsync("status");
            otherStatus.ShouldSucceedWith<StatusPayload>().Status.Target.ShouldBeNull();
        }
        finally
        {
            await other.RunAsync("daemon", "stop");
        }
    }

    [E2EFact]
    public async Task DaemonStop_WithoutARunningDaemon_StillSucceeds()
    {
        var runner = new CliRunner($"e2e-{Guid.NewGuid():N}");

        var result = await runner.RunAsync("daemon", "stop");

        result.ExitCode.ShouldBe(0);
        result.Response.ShouldNotBeNull();
        result.Response.Ok.ShouldBeTrue();
    }

    private static void WaitForProcessExit(int processId)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"Daemon process {processId} did not exit within 10s.");
    }
}

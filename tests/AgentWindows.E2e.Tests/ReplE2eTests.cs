using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.E2e.Tests;

public sealed class ReplE2eTests
{
    [E2EFact]
    public async Task Repl_DrivesAFullFlowOverOneProcess()
    {
        var runner = new CliRunner($"e2e-{Guid.NewGuid():N}");
        try
        {
            var (exitCode, responses) = await runner.RunReplAsync(
                "# comment lines are skipped",
                $"launch --app \"{TestPaths.TargetAppExecutable}\"",
                "snapshot -i",
                """{"cmd":"status"}""",
                "close --force",
                "exit"
            );

            exitCode.ShouldBe(0);
            responses.Count.ShouldBe(4);
            responses.ShouldAllBe(r => r.Ok);
            responses[0].Payload.ShouldBeOfType<WindowPayload>();
            var snapshot = responses[1].Payload.ShouldBeOfType<SnapshotPayload>();
            snapshot.Root.FindByAutomationId("SubmitButton").ShouldNotBeNull();
            responses[2].Payload.ShouldBeOfType<StatusPayload>();
        }
        finally
        {
            await runner.RunAsync("daemon", "stop");
        }
    }

    [E2EFact]
    public async Task Repl_FailsFastOnTheFirstError()
    {
        var runner = new CliRunner($"e2e-{Guid.NewGuid():N}");
        try
        {
            var (exitCode, responses) = await runner.RunReplAsync(
                "snapshot", // no target attached -> no-target failure
                "status" // must never run
            );

            exitCode.ShouldBe(1);
            var failure = responses.ShouldHaveSingleItem();
            failure.Ok.ShouldBeFalse();
            failure.ErrorCode.ShouldBe(ErrorCodes.NoTarget);
        }
        finally
        {
            await runner.RunAsync("daemon", "stop");
        }
    }
}

using AgentWindows.Core.Protocol.Capture;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Windows;
using AgentWindows.Core.Session;
using AgentWindows.E2E.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace AgentWindows.E2E.Tests.Scenarios;

public sealed class ReplE2eTests
{
    [E2EFact]
    public async Task Repl_DrivesAFullFlowOverOneProcess()
    {
        await using var session = new EphemeralCliSession();

        var (exitCode, responses) = await session.Cli.RunReplAsync(
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

    [E2EFact]
    public async Task Repl_FailsFastOnTheFirstError()
    {
        await using var session = new EphemeralCliSession();

        var (exitCode, responses) = await session.Cli.RunReplAsync(
            "snapshot", // no target attached -> no-target failure
            "status" // must never run
        );

        exitCode.ShouldBe(1);
        var failure = responses.ShouldHaveSingleItem();
        failure.Ok.ShouldBeFalse();
        failure.ErrorCode.ShouldBe(ErrorCodes.NoTarget);
    }
}

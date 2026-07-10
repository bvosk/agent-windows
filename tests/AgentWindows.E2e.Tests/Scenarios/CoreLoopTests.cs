using AgentWindows.Core.Protocol.Capture;
using AgentWindows.Core.Session;
using AgentWindows.E2E.Tests.Infrastructure;
using Shouldly;
using Xunit;

namespace AgentWindows.E2E.Tests.Scenarios;

public sealed class CoreLoopTests(TargetAppFixture fixture) : IClassFixture<TargetAppFixture>
{
    private readonly TargetAppFixture _fixture = fixture;

    [E2EFact]
    public async Task Snapshot_ExposesExpectedControlsWithRefs()
    {
        var snapshot = await _fixture.SnapshotAsync("-i");

        var input = snapshot.Root.RequireByAutomationId("InputBox");
        input.Role.ShouldBe("edit");
        input.Ref.ShouldNotBeNull();

        var submit = snapshot.Root.RequireByAutomationId("SubmitButton");
        submit.Role.ShouldBe("button");
        submit.Ref.ShouldNotBeNull();

        snapshot.Root.RequireByAutomationId("FeatureCheck").Role.ShouldBe("checkbox");
        snapshot.Root.RequireByAutomationId("ColorCombo").Role.ShouldBe("combobox");
        snapshot.Root.RequireByAutomationId("FruitsNode").Role.ShouldBe("treeitem");
    }

    [E2EFact]
    public async Task Fill_SetsTheValueVisibleInTheNextSnapshot()
    {
        var snapshot = await _fixture.SnapshotAsync("-i");
        var inputRef = snapshot.Root.RequireByAutomationId("InputBox").RequireRef();

        var fill = await _fixture.Cli.RunAsync("fill", $"@{inputRef}", "hello e2e");
        fill.ShouldSucceed();

        var after = await _fixture.SnapshotAsync("-i");
        after.Root.RequireByAutomationId("InputBox").Value.ShouldBe("hello e2e");
    }

    [E2EFact]
    public async Task Click_SubmitButton_UpdatesTheResultLabel()
    {
        var snapshot = await _fixture.SnapshotAsync("-i");
        var inputRef = snapshot.Root.RequireByAutomationId("InputBox").RequireRef();
        var submitRef = snapshot.Root.RequireByAutomationId("SubmitButton").RequireRef();

        (await _fixture.Cli.RunAsync("fill", $"@{inputRef}", "clicked")).ShouldSucceed();
        (await _fixture.Cli.RunAsync("click", $"@{submitRef}")).ShouldSucceed();

        var after = await _fixture.SnapshotAsync();
        after.Root.RequireByAutomationId("ResultLabel").Name.ShouldBe("Submitted: clicked");

        var wait = await _fixture.Cli.RunAsync(
            "wait",
            "--text",
            "SUBMITTED: CLICK",
            "--timeout",
            "5000"
        );
        wait.ShouldSucceed();

        var stillPresent = await _fixture.Cli.RunAsync(
            "wait",
            "--text",
            "Submitted: clicked",
            "--gone",
            "--timeout",
            "250"
        );
        stillPresent.ExitCode.ShouldBe(1);
        stillPresent.Response.ShouldNotBeNull();
        stillPresent.Response.ErrorCode.ShouldBe(ErrorCodes.Timeout);

        (
            await _fixture.Cli.RunAsync("wait", "--text", "Ready", "--gone", "--timeout", "5000")
        ).ShouldSucceed();
    }

    [E2EFact]
    public async Task Find_AppendsARef_AndActivateUsesSemanticPattern()
    {
        var snapshot = await _fixture.SnapshotAsync("-i");
        var inputRef = snapshot.Root.RequireByAutomationId("InputBox").RequireRef();

        var found = (
            await _fixture.Cli.RunAsync(
                "find",
                "--automation-id",
                "SubmitButton",
                "--require-unique"
            )
        ).ShouldSucceedWith<FindPayload>();
        found.Matches.ShouldHaveSingleItem().Ref.ShouldNotBeNull();

        (await _fixture.Cli.RunAsync("fill", $"@{inputRef}", "semantic")).ShouldSucceed();
        (
            await _fixture.Cli.RunAsync("activate", "--automation-id", "SubmitButton")
        ).ShouldSucceed();
        (
            await _fixture.Cli.RunAsync(
                "wait",
                "--text",
                "Submitted: semantic",
                "--timeout",
                "5000"
            )
        ).ShouldSucceed();
    }

    [E2EFact]
    public async Task Press_Enter_TriggersTheDefaultButton()
    {
        var snapshot = await _fixture.SnapshotAsync("-i");
        var inputRef = snapshot.Root.RequireByAutomationId("InputBox").RequireRef();

        (await _fixture.Cli.RunAsync("fill", $"@{inputRef}", "via enter")).ShouldSucceed();
        (await _fixture.Cli.RunAsync("click", $"@{inputRef}")).ShouldSucceed();
        (await _fixture.Cli.RunAsync("press", "Enter")).ShouldSucceed();

        var wait = await _fixture.Cli.RunAsync(
            "wait",
            "--text",
            "Submitted: via enter",
            "--timeout",
            "5000"
        );
        wait.ShouldSucceed();
    }

    [E2EFact]
    public async Task Wait_SlowTask_CompletesAfterDelay()
    {
        var snapshot = await _fixture.SnapshotAsync("-i");
        var slowRef = snapshot.Root.RequireByAutomationId("SlowButton").RequireRef();

        (await _fixture.Cli.RunAsync("click", $"@{slowRef}")).ShouldSucceed();

        var wait = await _fixture.Cli.RunAsync(
            "wait",
            "--text",
            "Done waiting",
            "--timeout",
            "10000"
        );
        wait.ShouldSucceed();
    }
}

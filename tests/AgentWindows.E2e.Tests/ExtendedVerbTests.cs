using Shouldly;
using Xunit;

namespace AgentWindows.E2e.Tests;

public sealed class ExtendedVerbTests(TargetAppFixture fixture) : IClassFixture<TargetAppFixture>
{
    private readonly TargetAppFixture _fixture = fixture;

    [E2EFact]
    public async Task Toggle_OnAndOff_IsIdempotent()
    {
        var checkRef = await RefOf("FeatureCheck");

        (await _fixture.Cli.RunAsync("toggle", $"@{checkRef}", "--on")).ShouldSucceed();
        (await StatesOf("FeatureCheck")).ShouldContain("checked");

        checkRef = await RefOf("FeatureCheck");
        (await _fixture.Cli.RunAsync("toggle", $"@{checkRef}", "--on")).ShouldSucceed();
        (await StatesOf("FeatureCheck")).ShouldContain("checked");

        checkRef = await RefOf("FeatureCheck");
        (await _fixture.Cli.RunAsync("toggle", $"@{checkRef}", "--off")).ShouldSucceed();
        (await StatesOf("FeatureCheck")).ShouldContain("unchecked");
    }

    [E2EFact]
    public async Task Select_ComboItem_ChangesTheValue()
    {
        var comboRef = await RefOf("ColorCombo");

        (await _fixture.Cli.RunAsync("select", $"@{comboRef}", "Blue")).ShouldSucceed();

        var snapshot = await _fixture.SnapshotAsync("-i");
        var combo = snapshot.Root.RequireByAutomationId("ColorCombo");
        combo.Value.ShouldBe("Blue");
    }

    [E2EFact]
    public async Task Expand_TreeNode_RevealsAndHidesChildren()
    {
        var nodeRef = await RefOf("FruitsNode");
        (await _fixture.Cli.RunAsync("expand", $"@{nodeRef}")).ShouldSucceed();

        var expanded = await _fixture.SnapshotAsync("-i");
        expanded.Root.FindByName("Apple").ShouldNotBeNull();
        expanded.Root.FindByName("Banana").ShouldNotBeNull();

        nodeRef = expanded.Root.RequireByAutomationId("FruitsNode").RequireRef();
        (await _fixture.Cli.RunAsync("expand", $"@{nodeRef}", "--collapse")).ShouldSucceed();

        var collapsed = await _fixture.SnapshotAsync("-i");
        collapsed.Root.RequireByAutomationId("FruitsNode").States.ShouldContain("collapsed");
    }

    [E2EFact]
    public async Task Scroll_List_Succeeds()
    {
        var listRef = await RefOf("BigList");

        var result = await _fixture.Cli.RunAsync("scroll", "down", $"@{listRef}", "--amount", "5");

        result.ShouldSucceed();
    }

    [E2EFact]
    public async Task Window_ResizeAndMove_UpdateTheReportedBounds()
    {
        var resize = await _fixture.Cli.RunAsync(
            "window",
            "resize",
            "--width",
            "600",
            "--height",
            "500"
        );
        var resized = resize.ShouldSucceedWith<Core.Protocol.WindowPayload>().Window;
        resized.Bounds.ShouldNotBeNull();
        resized.Bounds.Value.Width.ShouldBe(600);
        resized.Bounds.Value.Height.ShouldBe(500);

        var move = await _fixture.Cli.RunAsync("window", "move", "--x", "60", "--y", "60");
        var moved = move.ShouldSucceedWith<Core.Protocol.WindowPayload>().Window;
        moved.Bounds.ShouldNotBeNull();
        moved.Bounds.Value.X.ShouldBe(60);
        moved.Bounds.Value.Y.ShouldBe(60);
    }

    [E2EFact]
    public async Task Screenshot_Element_WritesAPngFile()
    {
        var buttonRef = await RefOf("SubmitButton");
        var path = Path.Combine(Path.GetTempPath(), $"aw-e2e-{Guid.NewGuid():N}.png");
        try
        {
            var result = await _fixture.Cli.RunAsync("screenshot", path, "--ref", $"@{buttonRef}");

            result.ShouldSucceedWith<Core.Protocol.ScreenshotPayload>().Path.ShouldBe(path);
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            bytes.Length.ShouldBeGreaterThan(100);
            bytes[1].ShouldBe((byte)'P');
            bytes[2].ShouldBe((byte)'N');
            bytes[3].ShouldBe((byte)'G');
        }
        finally
        {
            File.Delete(path);
        }
    }

    private async Task<string> RefOf(string automationId)
    {
        var snapshot = await _fixture.SnapshotAsync("-i");
        return snapshot.Root.RequireByAutomationId(automationId).RequireRef();
    }

    private async Task<IReadOnlyList<string>> StatesOf(string automationId)
    {
        var snapshot = await _fixture.SnapshotAsync("-i");
        return snapshot.Root.RequireByAutomationId(automationId).States;
    }
}

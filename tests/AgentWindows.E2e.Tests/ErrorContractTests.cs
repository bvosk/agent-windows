using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.E2e.Tests;

public sealed class ErrorContractTests(TargetAppFixture fixture) : IClassFixture<TargetAppFixture>
{
    private readonly TargetAppFixture _fixture = fixture;

    [E2EFact]
    public async Task Snapshot_WithoutTarget_ReturnsNoTarget()
    {
        await using var session = new EphemeralCliSession();

        var result = await session.Cli.RunAsync("snapshot");

        result.ExitCode.ShouldBe(1);
        result.Response.ShouldNotBeNull();
        result.Response.Ok.ShouldBeFalse();
        result.Response.ErrorCode.ShouldBe(ErrorCodes.NoTarget);
    }

    [E2EFact]
    public async Task Click_WithMalformedRef_ReturnsBadRequest()
    {
        var result = await _fixture.Cli.RunAsync("click", "banana");

        result.ExitCode.ShouldBe(1);
        result.Response.ShouldNotBeNull();
        result.Response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
    }

    [E2EFact]
    public async Task Click_WithNeverIssuedRef_ReturnsUnknownRef()
    {
        await _fixture.SnapshotAsync("-i");

        var result = await _fixture.Cli.RunAsync("click", "@e999999");

        result.ExitCode.ShouldBe(1);
        result.Response.ShouldNotBeNull();
        result.Response.ErrorCode.ShouldBe(ErrorCodes.UnknownRef);
    }

    [E2EFact]
    public async Task Click_WithRefFromAPreviousSnapshot_ReturnsStaleRef()
    {
        var first = await _fixture.SnapshotAsync("-i");
        var oldRef = first.Root.RequireByAutomationId("SubmitButton").RequireRef();

        await _fixture.SnapshotAsync("-i");
        var result = await _fixture.Cli.RunAsync("click", $"@{oldRef}");

        result.ExitCode.ShouldBe(1);
        result.Response.ShouldNotBeNull();
        result.Response.ErrorCode.ShouldBe(ErrorCodes.StaleRef);
        result.Response.Message.ShouldNotBeNull();
        result.Response.Message.ShouldContain("snapshot");
    }
}

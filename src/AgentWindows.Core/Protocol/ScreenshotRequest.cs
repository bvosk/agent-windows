namespace AgentWindows.Core.Protocol;

public sealed record ScreenshotRequest : DaemonRequest
{
    public string? Ref { get; init; }

    public required string OutputPath { get; init; }
}

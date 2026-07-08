namespace AgentWindows.Core.Protocol;

public sealed record ScreenshotPayload : ResponsePayload
{
    public required string Path { get; init; }
}

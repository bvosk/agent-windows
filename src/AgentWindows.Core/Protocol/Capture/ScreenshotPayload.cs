using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Capture;

public sealed record ScreenshotPayload : ResponsePayload
{
    public required string Path { get; init; }
}

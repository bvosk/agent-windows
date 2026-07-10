using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Capture;

public sealed record ScreenshotRequest : DaemonRequest
{
    public string? Ref { get; init; }

    public required string OutputPath { get; init; }
}

using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Capture;

public sealed record SnapshotRequest : DaemonRequest
{
    public bool InteractiveOnly { get; init; }

    public int? MaxDepth { get; init; }

    public string? ScopeRef { get; init; }
}

namespace AgentWindows.Core.Protocol;

public sealed record SnapshotRequest : DaemonRequest
{
    public bool InteractiveOnly { get; init; }

    public int? MaxDepth { get; init; }

    public string? ScopeRef { get; init; }
}

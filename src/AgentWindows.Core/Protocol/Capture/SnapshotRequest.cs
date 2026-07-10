using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Capture;

public sealed record SnapshotRequest : DaemonRequest
{
    public bool InteractiveOnly { get; init; }

    public int? MaxDepth { get; init; }

    public Snapshots.SnapshotView View { get; init; } = Snapshots.SnapshotView.Raw;

    public string? ScopeRef { get; init; }
}

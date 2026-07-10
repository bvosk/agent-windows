namespace AgentWindows.Core.Snapshots;

public sealed record SnapshotResult
{
    public required UiNode Root { get; init; }

    public required int Generation { get; init; }
}

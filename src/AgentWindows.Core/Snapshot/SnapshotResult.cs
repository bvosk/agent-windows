using AgentWindows.Core.Model;

namespace AgentWindows.Core.Snapshot;

public sealed record SnapshotResult
{
    public required UiNode Root { get; init; }

    public required int Generation { get; init; }
}

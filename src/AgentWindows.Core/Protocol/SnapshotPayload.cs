using AgentWindows.Core.Model;

namespace AgentWindows.Core.Protocol;

public sealed record SnapshotPayload : ResponsePayload
{
    public required UiNode Root { get; init; }

    public required int Generation { get; init; }
}

using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Snapshots;

namespace AgentWindows.Core.Protocol.Capture;

public sealed record SnapshotPayload : ResponsePayload
{
    public required UiNode Root { get; init; }

    public required int Generation { get; init; }
}

using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Snapshots;

namespace AgentWindows.Core.Protocol.Capture;

public sealed record FindPayload : ResponsePayload
{
    public required IReadOnlyList<UiNode> Matches { get; init; }
}

using AgentWindows.Core.Model;

namespace AgentWindows.Core.Protocol;

public sealed record WindowListPayload : ResponsePayload
{
    public required IReadOnlyList<WindowInfo> Windows { get; init; }
}

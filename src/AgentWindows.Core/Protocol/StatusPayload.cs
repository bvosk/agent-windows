using AgentWindows.Core.Session;

namespace AgentWindows.Core.Protocol;

public sealed record StatusPayload : ResponsePayload
{
    public required SessionStatus Status { get; init; }
}

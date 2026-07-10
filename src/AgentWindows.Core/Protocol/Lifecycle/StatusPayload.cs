using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Session;

namespace AgentWindows.Core.Protocol.Lifecycle;

public sealed record StatusPayload : ResponsePayload
{
    public required SessionStatus Status { get; init; }
}

using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Lifecycle;

public sealed record AckPayload : ResponsePayload
{
    public string? Detail { get; init; }
}

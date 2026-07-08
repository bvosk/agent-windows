using AgentWindows.Core.Model;

namespace AgentWindows.Core.Protocol;

public sealed record WindowPayload : ResponsePayload
{
    public required WindowInfo Window { get; init; }
}

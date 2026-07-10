using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Windows;

namespace AgentWindows.Core.Protocol.Windows;

public sealed record WindowPayload : ResponsePayload
{
    public required WindowInfo Window { get; init; }
}

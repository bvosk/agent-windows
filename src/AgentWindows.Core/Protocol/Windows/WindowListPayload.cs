using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Windows;

namespace AgentWindows.Core.Protocol.Windows;

public sealed record WindowListPayload : ResponsePayload
{
    public required IReadOnlyList<WindowInfo> Windows { get; init; }
}

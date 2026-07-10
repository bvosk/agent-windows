using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Interaction;

public sealed record PressRequest : DaemonRequest
{
    public required string Keys { get; init; }
}

using AgentWindows.Core.Input;
using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Interaction;

public sealed record ScrollRequest : DaemonRequest
{
    public string? Ref { get; init; }

    public required ScrollDirection Direction { get; init; }

    public double Amount { get; init; } = 1;

    public int TimeoutMs { get; init; } = ProtocolDefaults.TimeoutMs;
}

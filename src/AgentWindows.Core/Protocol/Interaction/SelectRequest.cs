using AgentWindows.Core.Elements;
using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Interaction;

public sealed record SelectRequest : DaemonRequest
{
    public string? Ref { get; init; }

    public ElementSelector? Selector { get; init; }

    public required string Item { get; init; }

    public int TimeoutMs { get; init; } = ProtocolDefaults.TimeoutMs;
}

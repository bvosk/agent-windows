using AgentWindows.Core.Elements;
using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Interaction;

public sealed record FillRequest : DaemonRequest
{
    public string? Ref { get; init; }

    public ElementSelector? Selector { get; init; }

    public required string Text { get; init; }

    public int TimeoutMs { get; init; } = ProtocolDefaults.TimeoutMs;
}

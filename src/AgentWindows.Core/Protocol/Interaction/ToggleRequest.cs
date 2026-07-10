using AgentWindows.Core.Elements;
using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Interaction;

public sealed record ToggleRequest : DaemonRequest
{
    public string? Ref { get; init; }

    public ElementSelector? Selector { get; init; }

    /// <summary>Null toggles; true/false force a target state.</summary>
    public bool? State { get; init; }

    public int TimeoutMs { get; init; } = ProtocolDefaults.TimeoutMs;
}

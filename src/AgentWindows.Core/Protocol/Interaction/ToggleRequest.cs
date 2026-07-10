using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Interaction;

public sealed record ToggleRequest : DaemonRequest
{
    public required string Ref { get; init; }

    /// <summary>Null toggles; true/false force a target state.</summary>
    public bool? State { get; init; }

    public int TimeoutMs { get; init; } = ProtocolDefaults.TimeoutMs;
}

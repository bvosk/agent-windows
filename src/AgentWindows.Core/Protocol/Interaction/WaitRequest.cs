using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Interaction;

public sealed record WaitRequest : DaemonRequest
{
    public string? Ref { get; init; }

    /// <summary>Waits for an element whose name contains this text to appear in the target.</summary>
    public string? Text { get; init; }

    public bool Gone { get; init; }

    public int TimeoutMs { get; init; } = ProtocolDefaults.TimeoutMs;
}

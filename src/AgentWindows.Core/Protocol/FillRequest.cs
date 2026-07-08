namespace AgentWindows.Core.Protocol;

public sealed record FillRequest : DaemonRequest
{
    public required string Ref { get; init; }

    public required string Text { get; init; }

    public int TimeoutMs { get; init; } = ProtocolDefaults.TimeoutMs;
}

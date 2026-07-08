namespace AgentWindows.Core.Protocol;

public sealed record SelectRequest : DaemonRequest
{
    public required string Ref { get; init; }

    public required string Item { get; init; }

    public int TimeoutMs { get; init; } = 10_000;
}

namespace AgentWindows.Core.Protocol;

public sealed record ExpandRequest : DaemonRequest
{
    public required string Ref { get; init; }

    public bool Collapse { get; init; }

    public int TimeoutMs { get; init; } = 10_000;
}

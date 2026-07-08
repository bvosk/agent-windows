using AgentWindows.Core.Model;

namespace AgentWindows.Core.Protocol;

public sealed record ScrollRequest : DaemonRequest
{
    public string? Ref { get; init; }

    public required ScrollDirection Direction { get; init; }

    public double Amount { get; init; } = 1;

    public int TimeoutMs { get; init; } = 10_000;
}

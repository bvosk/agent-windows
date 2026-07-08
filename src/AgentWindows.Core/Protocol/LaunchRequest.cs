namespace AgentWindows.Core.Protocol;

public sealed record LaunchRequest : DaemonRequest
{
    public required string Path { get; init; }

    public string? Arguments { get; init; }

    public int TimeoutMs { get; init; } = 10_000;
}

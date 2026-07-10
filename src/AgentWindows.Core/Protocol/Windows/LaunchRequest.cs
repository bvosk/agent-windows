using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Windows;

public sealed record LaunchRequest : DaemonRequest
{
    public required string Path { get; init; }

    public string? Arguments { get; init; }

    public int TimeoutMs { get; init; } = ProtocolDefaults.TimeoutMs;
}

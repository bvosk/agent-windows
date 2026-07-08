namespace AgentWindows.Core.Protocol;

public sealed record PressRequest : DaemonRequest
{
    public required string Keys { get; init; }
}

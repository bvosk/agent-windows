namespace AgentWindows.Core.Protocol;

public sealed record CloseRequest : DaemonRequest
{
    public bool Force { get; init; }
}

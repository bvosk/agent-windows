using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Windows;

public sealed record CloseRequest : DaemonRequest
{
    public bool Force { get; init; }
}

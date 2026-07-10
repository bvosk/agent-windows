using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Windows;

namespace AgentWindows.Core.Protocol.Windows;

public sealed record WindowActionRequest : DaemonRequest
{
    public required WindowActionKind Action { get; init; }

    public int? X { get; init; }

    public int? Y { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }
}

using AgentWindows.Core.Model;

namespace AgentWindows.Core.Protocol;

public sealed record WindowActionRequest : DaemonRequest
{
    public required WindowActionKind Action { get; init; }

    public int? X { get; init; }

    public int? Y { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }
}

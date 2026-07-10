using AgentWindows.Core.Input;
using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Interaction;

public sealed record ClickRequest : DaemonRequest
{
    public string? Ref { get; init; }

    public int? X { get; init; }

    public int? Y { get; init; }

    public MouseButtonKind Button { get; init; } = MouseButtonKind.Left;

    public bool DoubleClick { get; init; }

    public int TimeoutMs { get; init; } = ProtocolDefaults.TimeoutMs;
}

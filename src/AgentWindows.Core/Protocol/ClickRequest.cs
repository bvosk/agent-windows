using AgentWindows.Core.Model;

namespace AgentWindows.Core.Protocol;

public sealed record ClickRequest : DaemonRequest
{
    public string? Ref { get; init; }

    public int? X { get; init; }

    public int? Y { get; init; }

    public MouseButtonKind Button { get; init; } = MouseButtonKind.Left;

    public bool DoubleClick { get; init; }

    public int TimeoutMs { get; init; } = 10_000;
}

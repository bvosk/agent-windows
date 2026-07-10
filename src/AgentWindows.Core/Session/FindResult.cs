using AgentWindows.Core.Snapshots;

namespace AgentWindows.Core.Session;

public sealed record FindResult
{
    public required IReadOnlyList<UiNode> Matches { get; init; }
}

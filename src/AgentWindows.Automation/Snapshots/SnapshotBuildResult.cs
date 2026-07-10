using AgentWindows.Core.Snapshots;
using FlaUI.Core.AutomationElements;

namespace AgentWindows.Automation.Snapshots;

public sealed record SnapshotBuildResult
{
    public required UiNode Root { get; init; }

    public required IReadOnlyDictionary<string, AutomationElement> Refs { get; init; }

    public required int NextRefIndex { get; init; }
}

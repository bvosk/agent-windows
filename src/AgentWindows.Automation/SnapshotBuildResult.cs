using AgentWindows.Core.Model;
using FlaUI.Core.AutomationElements;

namespace AgentWindows.Automation;

public sealed record SnapshotBuildResult
{
    public required UiNode Root { get; init; }

    public required IReadOnlyDictionary<string, AutomationElement> Refs { get; init; }

    public required int NextRefIndex { get; init; }
}

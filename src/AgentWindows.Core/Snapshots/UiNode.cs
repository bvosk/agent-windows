using AgentWindows.Core.Windows;

namespace AgentWindows.Core.Snapshots;

public sealed record UiNode
{
    public required string Role { get; init; }

    public string? Name { get; init; }

    public string? AutomationId { get; init; }

    /// <summary>Bare ref id such as "e5"; rendered as "@e5". Null for non-interactive nodes.</summary>
    public string? Ref { get; init; }

    /// <summary>Settable: selection-derived values are patched in after the cached
    /// UIA walk closes (selection elements are not part of the bulk cache).</summary>
    public string? Value { get; set; }

    public IReadOnlyList<string> States { get; init; } = [];

    public BoundingRect? Bounds { get; init; }

    public IReadOnlyList<UiNode> Children { get; init; } = [];
}

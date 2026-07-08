namespace AgentWindows.Core.Model;

public sealed record UiNode
{
    public required string Role { get; init; }

    public string? Name { get; init; }

    public string? AutomationId { get; init; }

    /// <summary>Bare ref id such as "e5"; rendered as "@e5". Null for non-interactive nodes.</summary>
    public string? Ref { get; init; }

    public string? Value { get; init; }

    public IReadOnlyList<string> States { get; init; } = [];

    public BoundingRect? Bounds { get; init; }

    public IReadOnlyList<UiNode> Children { get; init; } = [];
}

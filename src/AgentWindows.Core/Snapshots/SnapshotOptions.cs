namespace AgentWindows.Core.Snapshots;

public sealed record SnapshotOptions
{
    public bool InteractiveOnly { get; init; }

    public int? MaxDepth { get; init; }

    public SnapshotView View { get; init; } = SnapshotView.Raw;

    /// <summary>Normalized ref of a previous snapshot's element to scope the walk to.</summary>
    public string? ScopeRef { get; init; }
}

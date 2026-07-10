namespace AgentWindows.Core.Windows;

public sealed record WindowInfo
{
    public required string Title { get; init; }

    public required long WindowHandle { get; init; }

    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    /// <summary>True when the owning process runs elevated; null when it cannot be determined.</summary>
    public bool? IsElevated { get; init; }

    public BoundingRect? Bounds { get; init; }
}

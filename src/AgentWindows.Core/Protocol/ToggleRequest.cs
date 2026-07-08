namespace AgentWindows.Core.Protocol;

public sealed record ToggleRequest : DaemonRequest
{
    public required string Ref { get; init; }

    /// <summary>Null toggles; true/false force a target state.</summary>
    public bool? State { get; init; }

    public int TimeoutMs { get; init; } = 10_000;
}

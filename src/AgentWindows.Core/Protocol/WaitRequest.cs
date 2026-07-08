namespace AgentWindows.Core.Protocol;

public sealed record WaitRequest : DaemonRequest
{
    public string? Ref { get; init; }

    /// <summary>Waits for an element whose name contains this text to appear in the target.</summary>
    public string? Text { get; init; }

    public bool Gone { get; init; }

    public int TimeoutMs { get; init; } = 10_000;
}

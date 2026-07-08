namespace AgentWindows.Core.Protocol;

public sealed record AttachRequest : DaemonRequest
{
    public string? Title { get; init; }

    public int? ProcessId { get; init; }

    public long? WindowHandle { get; init; }
}

namespace AgentWindows.Core.Protocol;

public sealed record AckPayload : ResponsePayload
{
    public string? Detail { get; init; }
}

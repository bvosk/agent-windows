namespace AgentWindows.Core.Elements;

public sealed record ElementSelector
{
    public string? AutomationId { get; init; }

    public string? Name { get; init; }

    public string? NameContains { get; init; }

    public string? Role { get; init; }

    public string? ScopeRef { get; init; }

    public bool RequireUnique { get; init; }

    public bool IsEmpty =>
        string.IsNullOrEmpty(AutomationId)
        && string.IsNullOrEmpty(Name)
        && string.IsNullOrEmpty(NameContains)
        && string.IsNullOrEmpty(Role);
}

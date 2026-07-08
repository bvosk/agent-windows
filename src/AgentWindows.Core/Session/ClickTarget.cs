namespace AgentWindows.Core.Session;

/// <summary>Either a normalized element ref, or screen coordinates as a fallback.</summary>
public sealed record ClickTarget
{
    private ClickTarget(string? elementRef, int? x, int? y)
    {
        ElementRef = elementRef;
        X = x;
        Y = y;
    }

    public string? ElementRef { get; }

    public int? X { get; }

    public int? Y { get; }

    public static ClickTarget ForRef(string elementRef) => new(elementRef, null, null);

    public static ClickTarget ForPoint(int x, int y) => new(null, x, y);
}

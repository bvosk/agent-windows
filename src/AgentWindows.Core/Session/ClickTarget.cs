using AgentWindows.Core.Elements;

namespace AgentWindows.Core.Session;

/// <summary>Either a ref/selector target, or screen coordinates as a fallback.</summary>
public sealed record ClickTarget
{
    private ClickTarget(string? elementRef, ElementSelector? selector, int? x, int? y)
    {
        ElementRef = elementRef;
        Selector = selector;
        X = x;
        Y = y;
    }

    public string? ElementRef { get; }

    public ElementSelector? Selector { get; }

    public int? X { get; }

    public int? Y { get; }

    public static ClickTarget ForRef(string elementRef) => new(elementRef, null, null, null);

    public static ClickTarget ForSelector(ElementSelector selector) =>
        new(null, selector, null, null);

    public static ClickTarget ForPoint(int x, int y) => new(null, null, x, y);
}

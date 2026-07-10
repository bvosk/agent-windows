using AgentWindows.Core.Elements;

namespace AgentWindows.Core.Session;

public sealed record ElementTarget
{
    private ElementTarget(string? elementRef, ElementSelector? selector)
    {
        ElementRef = elementRef;
        Selector = selector;
    }

    public string? ElementRef { get; }

    public ElementSelector? Selector { get; }

    public static ElementTarget ForRef(string elementRef) => new(elementRef, null);

    public static ElementTarget ForSelector(ElementSelector selector) => new(null, selector);
}

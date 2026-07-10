using AgentWindows.Core.Snapshots;

namespace AgentWindows.E2E.Tests.Infrastructure;

public static class SnapshotNodeExtensions
{
    public static IEnumerable<UiNode> Flatten(this UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return FlattenCore(node);
    }

    public static UiNode? FindByAutomationId(this UiNode node, string automationId) =>
        node.Flatten()
            .FirstOrDefault(n =>
                string.Equals(n.AutomationId, automationId, StringComparison.Ordinal)
            );

    public static UiNode? FindByName(this UiNode node, string name) =>
        node.Flatten().FirstOrDefault(n => string.Equals(n.Name, name, StringComparison.Ordinal));

    public static string RequireRef(this UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Ref
            ?? throw new InvalidOperationException(
                $"Element '{node.AutomationId ?? node.Name}' has no ref."
            );
    }

    public static UiNode RequireByAutomationId(this UiNode node, string automationId) =>
        node.FindByAutomationId(automationId)
        ?? throw new InvalidOperationException(
            $"No element with automationId '{automationId}' in the snapshot."
        );

    private static IEnumerable<UiNode> FlattenCore(UiNode node)
    {
        yield return node;
        foreach (var descendant in node.Children.SelectMany(FlattenCore))
        {
            yield return descendant;
        }
    }
}

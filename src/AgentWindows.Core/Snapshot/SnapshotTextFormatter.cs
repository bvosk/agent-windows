using System.Text;
using AgentWindows.Core.Model;

namespace AgentWindows.Core.Snapshot;

public static class SnapshotTextFormatter
{
    public static string Format(UiNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var builder = new StringBuilder();
        AppendNode(builder, root, 0);
        return builder.ToString();
    }

    private static void AppendNode(StringBuilder builder, UiNode node, int depth)
    {
        builder.Append(' ', depth * 2).Append("- ").Append(node.Role);
        if (!string.IsNullOrEmpty(node.Name))
        {
            builder.Append(" \"").Append(Escape(node.Name)).Append('"');
        }

        if (!string.IsNullOrEmpty(node.AutomationId))
        {
            builder.Append(" automationId=").Append(node.AutomationId);
        }

        foreach (var state in node.States)
        {
            builder.Append(' ').Append(state);
        }

        if (node.Value is not null)
        {
            builder.Append(" value=\"").Append(Escape(node.Value)).Append('"');
        }

        if (node.Ref is not null)
        {
            builder.Append(" [").Append(ElementRef.Display(node.Ref)).Append(']');
        }

        builder.Append('\n');
        foreach (var child in node.Children)
        {
            AppendNode(builder, child, depth + 1);
        }
    }

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}

using AgentWindows.Core.Model;
using AgentWindows.Core.Snapshot;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests;

public sealed class SnapshotTextFormatterTests
{
    [Fact]
    public void Format_RendersNestedTreeWithIndentation()
    {
        var root = new UiNode
        {
            Role = "window",
            Name = "Calculator",
            Ref = "e1",
            Children =
            [
                new UiNode
                {
                    Role = "button",
                    Name = "Five",
                    AutomationId = "num5Button",
                    Ref = "e2",
                },
                new UiNode
                {
                    Role = "group",
                    Name = "Display",
                    Children =
                    [
                        new UiNode
                        {
                            Role = "text",
                            Name = "Result",
                            Value = "0",
                        },
                    ],
                },
            ],
        };

        var text = SnapshotTextFormatter.Format(root);

        text.ShouldBe(
            """
            - window "Calculator" [@e1]
              - button "Five" automationId=num5Button [@e2]
              - group "Display"
                - text "Result" value="0"

            """.Replace("\r\n", "\n", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Format_RendersStatesBetweenAutomationIdAndValue()
    {
        var root = new UiNode
        {
            Role = "checkbox",
            Name = "Word wrap",
            Ref = "e3",
            States = ["focused", "checked"],
        };

        SnapshotTextFormatter
            .Format(root)
            .ShouldBe("- checkbox \"Word wrap\" focused checked [@e3]\n");
    }

    [Fact]
    public void Format_EscapesQuotesAndNewlinesInNamesAndValues()
    {
        var root = new UiNode
        {
            Role = "edit",
            Name = "say \"hi\"",
            Value = "line1\r\nline2",
        };

        SnapshotTextFormatter
            .Format(root)
            .ShouldBe("- edit \"say \\\"hi\\\"\" value=\"line1\\nline2\"\n");
    }

    [Fact]
    public void Format_OmitsEmptyNameAndMissingParts()
    {
        var root = new UiNode { Role = "pane" };

        SnapshotTextFormatter.Format(root).ShouldBe("- pane\n");
    }

    [Fact]
    public void Format_NullRoot_Throws() =>
        Should.Throw<ArgumentNullException>(() => SnapshotTextFormatter.Format(null!));
}

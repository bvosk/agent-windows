using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests;

public sealed class SkillCatalogTests
{
    [Fact]
    public void SkillNames_ContainsAgentWindows() =>
        SkillCatalog.SkillNames.ShouldContain("agent-windows");

    [Fact]
    public void DefaultSkillName_IsTheSoleBundledSkill() =>
        SkillCatalog.DefaultSkillName.ShouldBe("agent-windows");

    [Fact]
    public void Read_ReturnsSkillBodyWithFrontmatter()
    {
        var text = SkillCatalog.Read("agent-windows", full: false);

        text.ShouldNotBeNull();
        text.ShouldStartWith("---");
        text.ShouldContain("name: agent-windows");
        text.ShouldContain("## The core loop");
    }

    [Fact]
    public void Read_Full_AppendsEveryReferenceFile()
    {
        var text = SkillCatalog.Read("agent-windows", full: true);

        text.ShouldNotBeNull();
        text.ShouldContain("<!-- references/commands.md -->");
        text.ShouldContain("<!-- references/snapshot-refs.md -->");
        text.ShouldContain("<!-- references/repl-batch.md -->");
        text.ShouldContain("<!-- references/windows-gotchas.md -->");
        text.ShouldContain("# Command reference");
    }

    [Fact]
    public void Read_Full_KeepsSkillBodyBeforeReferences()
    {
        var text = SkillCatalog.Read("agent-windows", full: true)!;

        text.IndexOf("## The core loop", StringComparison.Ordinal)
            .ShouldBeLessThan(text.IndexOf("# Command reference", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_UnknownSkill_ReturnsNull() =>
        SkillCatalog.Read("nonexistent", full: false).ShouldBeNull();
}

using AgentWindows.Core.Elements;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests.Elements;

public sealed class ElementSelectorTests
{
    [Fact]
    public void IsEmpty_IgnoresScopeAndUniqueness()
    {
        new ElementSelector().IsEmpty.ShouldBeTrue();
        new ElementSelector { ScopeRef = "e1", RequireUnique = true }.IsEmpty.ShouldBeTrue();
    }

    [Theory]
    [InlineData("automation-id")]
    [InlineData("name")]
    [InlineData("name-contains")]
    [InlineData("role")]
    public void IsEmpty_IsFalseForEverySearchField(string field)
    {
        var selector = field switch
        {
            "automation-id" => new ElementSelector { AutomationId = "save" },
            "name" => new ElementSelector { Name = "Save" },
            "name-contains" => new ElementSelector { NameContains = "Sav" },
            _ => new ElementSelector { Role = "button" },
        };

        selector.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void ElementTarget_FactoriesSelectExactlyOneAddressingMode()
    {
        var selector = new ElementSelector { AutomationId = "save" };

        var byRef = ElementTarget.ForRef("e3");
        byRef.ElementRef.ShouldBe("e3");
        byRef.Selector.ShouldBeNull();

        var bySelector = ElementTarget.ForSelector(selector);
        bySelector.ElementRef.ShouldBeNull();
        bySelector.Selector.ShouldBeSameAs(selector);
    }

    [Fact]
    public void ClickTarget_SelectorFactoryPreservesSelector()
    {
        var selector = new ElementSelector { Role = "button" };

        var target = ClickTarget.ForSelector(selector);

        target.ElementRef.ShouldBeNull();
        target.Selector.ShouldBeSameAs(selector);
        target.X.ShouldBeNull();
        target.Y.ShouldBeNull();
    }
}

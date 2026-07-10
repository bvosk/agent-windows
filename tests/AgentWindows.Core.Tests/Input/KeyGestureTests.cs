using AgentWindows.Core.Input;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests.Input;

public sealed class KeyGestureTests
{
    [Fact]
    public void Parse_SingleNamedKey()
    {
        var gesture = KeyGesture.Parse("Enter");

        gesture.Modifiers.ShouldBeEmpty();
        gesture.Key.ShouldBe("Enter");
    }

    [Fact]
    public void Parse_PreservesTheKeyTokenAsWritten()
    {
        // Which named keys exist is the automation layer's decision; parsing
        // only validates syntax and keeps the token verbatim.
        KeyGesture.Parse("enter").Key.ShouldBe("enter");
        KeyGesture.Parse("PAGEUP").Key.ShouldBe("PAGEUP");
        KeyGesture.Parse("NotAKnownKey").Key.ShouldBe("NotAKnownKey");
    }

    [Fact]
    public void Parse_ChordWithModifiers()
    {
        var gesture = KeyGesture.Parse("Ctrl+Shift+S");

        gesture.Modifiers.ShouldBe([KeyModifier.Ctrl, KeyModifier.Shift]);
        gesture.Key.ShouldBe("S");
    }

    [Fact]
    public void Parse_ModifierNamesAreFlexible()
    {
        KeyGesture.Parse("Control+A").Modifiers.ShouldBe([KeyModifier.Ctrl]);
        KeyGesture.Parse("Windows+R").Modifiers.ShouldBe([KeyModifier.Win]);
        KeyGesture.Parse("meta+L").Modifiers.ShouldBe([KeyModifier.Win]);
    }

    [Fact]
    public void Parse_DuplicateModifiersAreDeduplicated() =>
        KeyGesture.Parse("Ctrl+Control+C").Modifiers.ShouldBe([KeyModifier.Ctrl]);

    [Fact]
    public void Parse_SingleCharacterPreservesCase()
    {
        KeyGesture.Parse("a").Key.ShouldBe("a");
        KeyGesture.Parse("A").Key.ShouldBe("A");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Bad+S")]
    [InlineData("Enter+S")]
    public void TryParse_RejectsInvalidSyntax(string input) =>
        KeyGesture.TryParse(input, out _).ShouldBeFalse();

    [Fact]
    public void Parse_InvalidSyntax_ThrowsBadRequest()
    {
        var exception = Should.Throw<AutomationException>(() => KeyGesture.Parse("Ctrl+"));
        exception.Code.ShouldBe(ErrorCodes.BadRequest);
    }
}

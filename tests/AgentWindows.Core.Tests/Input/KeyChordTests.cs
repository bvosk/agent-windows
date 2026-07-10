using AgentWindows.Core.Input;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests.Input;

public sealed class KeyChordTests
{
    [Fact]
    public void Parse_SingleNamedKey()
    {
        var chord = KeyChord.Parse("Enter");

        chord.Modifiers.ShouldBeEmpty();
        chord.Key.ShouldBe("Enter");
    }

    [Fact]
    public void Parse_PreservesTheKeyTokenAsWritten()
    {
        // Which named keys exist is the automation layer's decision; parsing
        // only validates syntax and keeps the token verbatim.
        KeyChord.Parse("enter").Key.ShouldBe("enter");
        KeyChord.Parse("PAGEUP").Key.ShouldBe("PAGEUP");
        KeyChord.Parse("NotAKnownKey").Key.ShouldBe("NotAKnownKey");
    }

    [Fact]
    public void Parse_ChordWithModifiers()
    {
        var chord = KeyChord.Parse("Ctrl+Shift+S");

        chord.Modifiers.ShouldBe([KeyModifier.Ctrl, KeyModifier.Shift]);
        chord.Key.ShouldBe("S");
    }

    [Fact]
    public void Parse_ModifierNamesAreFlexible()
    {
        KeyChord.Parse("Control+A").Modifiers.ShouldBe([KeyModifier.Ctrl]);
        KeyChord.Parse("Windows+R").Modifiers.ShouldBe([KeyModifier.Win]);
        KeyChord.Parse("meta+L").Modifiers.ShouldBe([KeyModifier.Win]);
    }

    [Fact]
    public void Parse_DuplicateModifiersAreDeduplicated() =>
        KeyChord.Parse("Ctrl+Control+C").Modifiers.ShouldBe([KeyModifier.Ctrl]);

    [Fact]
    public void Parse_SingleCharacterPreservesCase()
    {
        KeyChord.Parse("a").Key.ShouldBe("a");
        KeyChord.Parse("A").Key.ShouldBe("A");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Bad+S")]
    [InlineData("Enter+S")]
    public void TryParse_RejectsInvalidSyntax(string input) =>
        KeyChord.TryParse(input, out _).ShouldBeFalse();

    [Fact]
    public void Parse_InvalidSyntax_ThrowsBadRequest()
    {
        var exception = Should.Throw<AutomationException>(() => KeyChord.Parse("Ctrl+"));
        exception.Code.ShouldBe(ErrorCodes.BadRequest);
    }
}

using AgentWindows.Automation.Input;
using FlaUI.Core.WindowsAPI;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests.Automation;

public sealed class KeyMapperTests
{
    [Theory]
    [InlineData("Enter", VirtualKeyShort.RETURN)]
    [InlineData("enter", VirtualKeyShort.RETURN)]
    [InlineData("Return", VirtualKeyShort.RETURN)]
    [InlineData("Esc", VirtualKeyShort.ESCAPE)]
    [InlineData("Escape", VirtualKeyShort.ESCAPE)]
    [InlineData("Del", VirtualKeyShort.DELETE)]
    [InlineData("PgUp", VirtualKeyShort.PRIOR)]
    [InlineData("pagedown", VirtualKeyShort.NEXT)]
    [InlineData("F5", VirtualKeyShort.F5)]
    public void TryMapKey_ResolvesNamedKeysAndAliasesCaseInsensitively(
        string key,
        VirtualKeyShort expected
    )
    {
        KeyMapper.TryMapKey(key, out var virtualKey).ShouldBeTrue();
        virtualKey.ShouldBe(expected);
    }

    [Theory]
    [InlineData("a", VirtualKeyShort.KEY_A)]
    [InlineData("Z", VirtualKeyShort.KEY_Z)]
    [InlineData("0", VirtualKeyShort.KEY_0)]
    [InlineData("9", VirtualKeyShort.KEY_9)]
    public void TryMapKey_ResolvesCharacters(string key, VirtualKeyShort expected)
    {
        KeyMapper.TryMapKey(key, out var virtualKey).ShouldBeTrue();
        virtualKey.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("+")]
    public void TryMapKey_RejectsUnknownKeys(string key) =>
        KeyMapper.TryMapKey(key, out _).ShouldBeFalse();
}

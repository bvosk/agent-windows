using AgentWindows.Core.Elements;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests.Elements;

public sealed class ElementRefTests
{
    [Theory]
    [InlineData("@e5", "e5")]
    [InlineData("e5", "e5")]
    [InlineData("@e123", "e123")]
    [InlineData("e1", "e1")]
    public void TryNormalize_AcceptsValidRefs(string input, string expected)
    {
        ElementRef.TryNormalize(input, out var normalized).ShouldBeTrue();
        normalized.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("e")]
    [InlineData("@e")]
    [InlineData("@5")]
    [InlineData("x5")]
    [InlineData("e5a")]
    [InlineData("@@e5")]
    [InlineData("E5")]
    public void TryNormalize_RejectsInvalidRefs(string? input) =>
        ElementRef.TryNormalize(input, out _).ShouldBeFalse();

    [Fact]
    public void Display_PrefixesWithAtSign() => ElementRef.Display("e7").ShouldBe("@e7");

    [Theory]
    [InlineData("e0", 0)]
    [InlineData("e5", 5)]
    [InlineData("e2147483647", int.MaxValue)]
    public void TryGetIndex_ExtractsNumericSuffix(string bareRef, int expected)
    {
        ElementRef.TryGetIndex(bareRef, out var index).ShouldBeTrue();
        index.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("x5")]
    [InlineData("e-1")]
    [InlineData("e2147483648")]
    public void TryGetIndex_RejectsInvalidBareRefs(string? bareRef)
    {
        ElementRef.TryGetIndex(bareRef!, out var index).ShouldBeFalse();
        index.ShouldBe(0);
    }
}

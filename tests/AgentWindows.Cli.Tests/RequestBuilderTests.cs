using AgentWindows.Cli;
using AgentWindows.Core.Model;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests;

public sealed class RequestBuilderTests
{
    [Fact]
    public void ParsePoint_ParsesCoordinates()
    {
        RequestBuilder.ParsePoint("640,220").ShouldBe((640, 220));
        RequestBuilder.ParsePoint(" 10 , -5 ").ShouldBe((10, -5));
    }

    [Theory]
    [InlineData("640")]
    [InlineData("a,b")]
    [InlineData("1,b")]
    [InlineData("1,2,3")]
    [InlineData("")]
    public void ParsePoint_RejectsMalformedInput(string at)
    {
        var exception = Should.Throw<AutomationException>(() => RequestBuilder.ParsePoint(at));
        exception.Code.ShouldBe(ErrorCodes.BadRequest);
    }

    [Theory]
    [InlineData("up", ScrollDirection.Up)]
    [InlineData("DOWN", ScrollDirection.Down)]
    [InlineData("Left", ScrollDirection.Left)]
    [InlineData("right", ScrollDirection.Right)]
    public void ParseDirection_IsCaseInsensitive(string input, ScrollDirection expected) =>
        RequestBuilder.ParseDirection(input).ShouldBe(expected);

    [Fact]
    public void ParseDirection_RejectsUnknownDirection() =>
        Should
            .Throw<AutomationException>(() => RequestBuilder.ParseDirection("sideways"))
            .Code.ShouldBe(ErrorCodes.BadRequest);

    [Theory]
    [InlineData("focus", WindowActionKind.Focus)]
    [InlineData("MAXIMIZE", WindowActionKind.Maximize)]
    [InlineData("restore", WindowActionKind.Restore)]
    public void ParseWindowAction_IsCaseInsensitive(string input, WindowActionKind expected) =>
        RequestBuilder.ParseWindowAction(input).ShouldBe(expected);

    [Fact]
    public void ParseWindowAction_RejectsUnknownAction() =>
        Should
            .Throw<AutomationException>(() => RequestBuilder.ParseWindowAction("explode"))
            .Code.ShouldBe(ErrorCodes.BadRequest);

    [Fact]
    public void BuildClick_MapsCoordinatesAndButton()
    {
        var request = RequestBuilder.BuildClick(null, "5,7", true, false, true, 2000);

        request.X.ShouldBe(5);
        request.Y.ShouldBe(7);
        request.Button.ShouldBe(MouseButtonKind.Right);
        request.DoubleClick.ShouldBeTrue();
        request.TimeoutMs.ShouldBe(2000);
    }

    [Fact]
    public void BuildClick_MiddleButton()
    {
        RequestBuilder
            .BuildClick("@e1", null, false, true, false, 1000)
            .Button.ShouldBe(MouseButtonKind.Middle);
    }

    [Fact]
    public void BuildClick_DefaultsToLeftButton()
    {
        var request = RequestBuilder.BuildClick("@e1", null, false, false, false, 1000);

        request.Ref.ShouldBe("@e1");
        request.Button.ShouldBe(MouseButtonKind.Left);
    }

    [Fact]
    public void BuildToggle_RejectsOnAndOffTogether() =>
        Should
            .Throw<AutomationException>(() => RequestBuilder.BuildToggle("@e1", true, true, 1000))
            .Code.ShouldBe(ErrorCodes.BadRequest);

    [Fact]
    public void BuildToggle_MapsDesiredState()
    {
        RequestBuilder.BuildToggle("@e1", true, false, 1000).State.ShouldBe(true);
        RequestBuilder.BuildToggle("@e1", false, true, 1000).State.ShouldBe(false);
        RequestBuilder.BuildToggle("@e1", false, false, 1000).State.ShouldBeNull();
    }
}

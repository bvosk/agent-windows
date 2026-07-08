using AgentWindows.Core.Dispatch;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;
using AgentWindows.Core.Tests.Fakes;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests;

public sealed class RequestDispatcherTests : IDisposable
{
    private readonly FakeAutomationSession _session = new();
    private readonly RequestDispatcher _dispatcher;

    public RequestDispatcherTests()
    {
        _dispatcher = new RequestDispatcher(_session);
    }

    public void Dispose() => _session.Dispose();

    [Fact]
    public void Dispatch_NullRequest_Throws() =>
        Should.Throw<ArgumentNullException>(() => _dispatcher.Dispatch(null!));

    [Fact]
    public void ListWindows_ReturnsWindowListPayload()
    {
        var response = _dispatcher.Dispatch(new ListWindowsRequest());

        response.Ok.ShouldBeTrue();
        var payload = response.Payload.ShouldBeOfType<WindowListPayload>();
        payload.Windows.ShouldHaveSingleItem().Title.ShouldBe("Test Window");
        _session.Calls.ShouldBe(["list"]);
    }

    [Fact]
    public void Launch_PassesArgumentsAndTimeout()
    {
        var response = _dispatcher.Dispatch(
            new LaunchRequest
            {
                Path = "notepad.exe",
                Arguments = "a.txt",
                TimeoutMs = 5000,
            }
        );

        response.Ok.ShouldBeTrue();
        response.Payload.ShouldBeOfType<WindowPayload>();
        _session.Calls.ShouldBe(["launch path=notepad.exe args=a.txt timeout=5000"]);
    }

    [Fact]
    public void Snapshot_NormalizesScopeRef()
    {
        var response = _dispatcher.Dispatch(
            new SnapshotRequest { InteractiveOnly = true, ScopeRef = "@e3" }
        );

        response.Ok.ShouldBeTrue();
        response.Payload.ShouldBeOfType<SnapshotPayload>().Generation.ShouldBe(1);
        _session.Calls.ShouldBe(["snapshot interactive=True depth= scope=e3"]);
    }

    [Fact]
    public void Click_WithRef_NormalizesRef()
    {
        var response = _dispatcher.Dispatch(new ClickRequest { Ref = "@e5" });

        response.Ok.ShouldBeTrue();
        response.Payload.ShouldBeOfType<AckPayload>().Detail.ShouldBe("clicked");
        _session
            .Calls.ShouldHaveSingleItem()
            .ShouldBe("click ref=e5 x= y= button=Left double=False timeout=10000");
    }

    [Fact]
    public void Click_WithCoordinates_PassesPoint()
    {
        var response = _dispatcher.Dispatch(new ClickRequest { X = 10, Y = 20 });

        response.Ok.ShouldBeTrue();
        _session
            .Calls.ShouldHaveSingleItem()
            .ShouldBe("click ref= x=10 y=20 button=Left double=False timeout=10000");
    }

    [Fact]
    public void Click_WithoutRefOrCoordinates_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(new ClickRequest());

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Click_WithInvalidRef_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(new ClickRequest { Ref = "banana" });

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        response.Message.ShouldNotBeNull();
        response.Message.ShouldContain("banana");
    }

    [Fact]
    public void Fill_PassesTextThrough()
    {
        var response = _dispatcher.Dispatch(new FillRequest { Ref = "@e7", Text = "hello" });

        response.Ok.ShouldBeTrue();
        _session.Calls.ShouldBe(["fill ref=e7 text=hello timeout=10000"]);
    }

    [Fact]
    public void Press_ParsesGestureBeforeCallingSession()
    {
        var response = _dispatcher.Dispatch(new PressRequest { Keys = "Ctrl+Shift+P" });

        response.Ok.ShouldBeTrue();
        _session.Calls.ShouldBe(["press modifiers=Ctrl+Shift key=P"]);
    }

    [Fact]
    public void Press_InvalidGestureSyntax_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(new PressRequest { Keys = "Ctrl+" });

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Wait_WithoutRefOrText_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(new WaitRequest());

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
    }

    [Fact]
    public void Wait_WithText_PassesThrough()
    {
        var response = _dispatcher.Dispatch(new WaitRequest { Text = "Saved", Gone = true });

        response.Ok.ShouldBeTrue();
        _session.Calls.ShouldBe(["wait ref= text=Saved gone=True timeout=10000"]);
    }

    [Fact]
    public void NonPositiveTimeout_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(
            new FillRequest
            {
                Ref = "@e1",
                Text = "x",
                TimeoutMs = 0,
            }
        );

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
    }

    [Fact]
    public void AutomationException_BecomesTypedFailure()
    {
        _session.ThrowOnNextCall = new AutomationException(
            ErrorCodes.StaleRef,
            "take a new snapshot"
        );

        var response = _dispatcher.Dispatch(new FillRequest { Ref = "@e1", Text = "x" });

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.StaleRef);
        response.Message.ShouldBe("take a new snapshot");
    }

    [Fact]
    public void UnexpectedException_BecomesInternalError()
    {
        _session.ThrowOnNextCall = new InvalidOperationException("boom");

        var response = _dispatcher.Dispatch(new ListWindowsRequest());

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.InternalError);
        response.Message.ShouldBe("boom");
    }

    [Fact]
    public void Status_ReturnsStatusPayload()
    {
        var response = _dispatcher.Dispatch(new StatusRequest());

        response.Ok.ShouldBeTrue();
        var payload = response.Payload.ShouldBeOfType<StatusPayload>();
        payload.Status.DaemonProcessId.ShouldBe(99);
        payload.Status.RefCount.ShouldBe(3);
    }

    [Fact]
    public void Shutdown_AcksWithoutTouchingSession()
    {
        var response = _dispatcher.Dispatch(new ShutdownRequest());

        response.Ok.ShouldBeTrue();
        response.Payload.ShouldBeOfType<AckPayload>().Detail.ShouldBe("shutting down");
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Attach_WithoutAnySelector_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(new AttachRequest());

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void WindowMove_WithoutCoordinates_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(
            new WindowActionRequest { Action = Core.Model.WindowActionKind.Move, X = 10 }
        );

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void WindowResize_WithoutDimensions_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(
            new WindowActionRequest { Action = Core.Model.WindowActionKind.Resize }
        );

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
    }

    [Fact]
    public void Screenshot_WithRelativePath_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(new ScreenshotRequest { OutputPath = "shot.png" });

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void SelectExpandToggleScrollScreenshotCloseWindow_RouteToSession()
    {
        _dispatcher.Dispatch(new SelectRequest { Ref = "@e1", Item = "Red" });
        _dispatcher.Dispatch(new ExpandRequest { Ref = "@e2", Collapse = true });
        _dispatcher.Dispatch(new ToggleRequest { Ref = "@e3", State = true });
        _dispatcher.Dispatch(
            new ScrollRequest { Ref = "@e4", Direction = Core.Model.ScrollDirection.Down }
        );
        _dispatcher.Dispatch(new ScreenshotRequest { OutputPath = @"C:\shots\shot.png" });
        _dispatcher.Dispatch(
            new WindowActionRequest { Action = Core.Model.WindowActionKind.Maximize }
        );
        _dispatcher.Dispatch(new CloseRequest { Force = true });

        _session.Calls.ShouldBe([
            "select ref=e1 item=Red timeout=10000",
            "expand ref=e2 collapse=True timeout=10000",
            "toggle ref=e3 state=True timeout=10000",
            "scroll ref=e4 direction=Down amount=1 timeout=10000",
            @"screenshot ref= path=C:\shots\shot.png",
            "window action=Maximize x= y= width= height=",
            "close force=True",
        ]);
    }
}

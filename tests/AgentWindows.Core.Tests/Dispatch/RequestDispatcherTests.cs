using AgentWindows.Core.Dispatch;
using AgentWindows.Core.Elements;
using AgentWindows.Core.Input;
using AgentWindows.Core.Protocol.Capture;
using AgentWindows.Core.Protocol.Interaction;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Protocol.Windows;
using AgentWindows.Core.Session;
using AgentWindows.Core.Tests.Fakes;
using AgentWindows.Core.Windows;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests.Dispatch;

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
    public void Click_WithSelector_RoutesDirectlyToSession()
    {
        var response = _dispatcher.Dispatch(
            new ClickRequest { Selector = new ElementSelector { Name = "Save" } }
        );

        response.Ok.ShouldBeTrue();
        _session.Calls.ShouldBe(["click ref= x= y= button=Left double=False timeout=10000"]);
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Click_WithConflictingOrPartialTarget_FailsWithBadRequest(
        bool includeRef,
        bool includeSelector,
        bool includeX,
        bool includeY
    )
    {
        var response = _dispatcher.Dispatch(
            new ClickRequest
            {
                Ref = includeRef ? "e1" : null,
                Selector = includeSelector ? new ElementSelector { Role = "button" } : null,
                X = includeX ? 10 : null,
                Y = includeY ? 20 : null,
            }
        );

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
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
        _session.Calls.ShouldBe(["fill target=e7 text=hello timeout=10000"]);
    }

    [Fact]
    public void Press_ParsesChordBeforeCallingSession()
    {
        var response = _dispatcher.Dispatch(new PressRequest { Keys = "Ctrl+Shift+P" });

        response.Ok.ShouldBeTrue();
        _session.Calls.ShouldBe(["press modifiers=Ctrl+Shift key=P"]);
    }

    [Fact]
    public void Press_InvalidChordSyntax_FailsWithBadRequest()
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
        _session.Calls.ShouldBe(["wait target=none text=Saved gone=True timeout=10000"]);
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
    public void UnsupportedRequest_FailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(new UnknownRequest());

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        response.Message.ShouldBe("Unsupported request type 'UnknownRequest'.");
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
    public void Attach_WithEachSelector_RoutesToSession()
    {
        var byTitle = _dispatcher.Dispatch(new AttachRequest { Title = "Editor" });
        var byProcessId = _dispatcher.Dispatch(new AttachRequest { ProcessId = 42 });
        var byWindowHandle = _dispatcher.Dispatch(new AttachRequest { WindowHandle = 0x1234 });

        byTitle.Ok.ShouldBeTrue();
        byProcessId.Ok.ShouldBeTrue();
        byWindowHandle.Ok.ShouldBeTrue();
        _session.Calls.ShouldBe([
            "attach title=Editor pid= hwnd=",
            "attach title= pid=42 hwnd=",
            "attach title= pid= hwnd=4660",
        ]);
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(null, 20)]
    public void WindowMove_WithoutEitherCoordinate_FailsWithBadRequest(int? x, int? y)
    {
        var response = _dispatcher.Dispatch(
            new WindowActionRequest
            {
                Action = Core.Windows.WindowActionKind.Move,
                X = x,
                Y = y,
            }
        );

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, 20)]
    [InlineData(10, null)]
    public void WindowResize_WithoutEitherDimension_FailsWithBadRequest(int? width, int? height)
    {
        var response = _dispatcher.Dispatch(
            new WindowActionRequest
            {
                Action = Core.Windows.WindowActionKind.Resize,
                Width = width,
                Height = height,
            }
        );

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
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
        _dispatcher.Dispatch(new ExpandRequest { Ref = "@e2", Collapse = false });
        _dispatcher.Dispatch(new ToggleRequest { Ref = "@e3", State = true });
        _dispatcher.Dispatch(
            new ScrollRequest { Ref = "@e4", Direction = Core.Input.ScrollDirection.Down }
        );
        _dispatcher.Dispatch(new ScreenshotRequest { OutputPath = @"C:\shots\shot.png" });
        _dispatcher.Dispatch(
            new WindowActionRequest { Action = Core.Windows.WindowActionKind.Maximize }
        );
        _dispatcher.Dispatch(
            new WindowActionRequest
            {
                Action = Core.Windows.WindowActionKind.Move,
                X = 10,
                Y = 20,
            }
        );
        _dispatcher.Dispatch(
            new WindowActionRequest
            {
                Action = Core.Windows.WindowActionKind.Resize,
                Width = 800,
                Height = 600,
            }
        );
        _dispatcher.Dispatch(new CloseRequest { Force = true });

        _session.Calls.ShouldBe([
            "select target=e1 item=Red timeout=10000",
            "expand target=e2 collapse=True timeout=10000",
            "expand target=e2 collapse=False timeout=10000",
            "toggle target=e3 state=True timeout=10000",
            "scroll target=e4 direction=Down amount=1 timeout=10000",
            @"screenshot ref= path=C:\shots\shot.png",
            "window action=Maximize x= y= width= height=",
            "window action=Move x=10 y=20 width= height=",
            "window action=Resize x= y= width=800 height=600",
            "close force=True",
        ]);
    }

    [Fact]
    public void Wait_WithRef_NormalizesAndPassesThrough()
    {
        var response = _dispatcher.Dispatch(new WaitRequest { Ref = "@e8" });

        response.Ok.ShouldBeTrue();
        _session.Calls.ShouldBe(["wait target=e8 text= gone=False timeout=10000"]);
    }

    [Fact]
    public void Find_NormalizesScopeAndReturnsMatches()
    {
        var response = _dispatcher.Dispatch(
            new FindRequest
            {
                Selector = new ElementSelector
                {
                    AutomationId = "SubmitButton",
                    ScopeRef = "@e3",
                    RequireUnique = true,
                },
            }
        );

        response.Ok.ShouldBeTrue();
        response.Payload.ShouldBeOfType<FindPayload>().Matches.ShouldHaveSingleItem();
        _session.Calls.ShouldBe([
            "find selector=id=SubmitButton,name=,contains=,role=,scope=e3,unique=True all=False",
        ]);
    }

    [Fact]
    public void Activate_WithSelectorRoutesDirectlyToSession()
    {
        var response = _dispatcher.Dispatch(
            new ActivateRequest { Selector = new ElementSelector { AutomationId = "SubmitButton" } }
        );

        response.Ok.ShouldBeTrue();
        _session.Calls.ShouldBe([
            "activate target=id=SubmitButton,name=,contains=,role=,scope=,unique=False timeout=10000",
        ]);
    }

    [Fact]
    public void Activate_WithoutTargetFailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(new ActivateRequest());

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Action_WithRefAndSelectorFailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(
            new FillRequest
            {
                Ref = "e1",
                Selector = new ElementSelector { AutomationId = "InputBox" },
                Text = "hello",
            }
        );

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void EmptySelectorFailsWithBadRequest()
    {
        var response = _dispatcher.Dispatch(new FindRequest { Selector = new ElementSelector() });

        response.Ok.ShouldBeFalse();
        response.ErrorCode.ShouldBe(ErrorCodes.BadRequest);
        _session.Calls.ShouldBeEmpty();
    }

    private sealed record UnknownRequest : DaemonRequest;
}

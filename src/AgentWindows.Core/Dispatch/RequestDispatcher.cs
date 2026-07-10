using System.Diagnostics.CodeAnalysis;
using AgentWindows.Core.Elements;
using AgentWindows.Core.Input;
using AgentWindows.Core.Protocol.Capture;
using AgentWindows.Core.Protocol.Interaction;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Protocol.Windows;
using AgentWindows.Core.Session;
using AgentWindows.Core.Snapshots;
using AgentWindows.Core.Windows;

namespace AgentWindows.Core.Dispatch;

public sealed class RequestDispatcher(IAutomationSession session)
{
    private readonly IAutomationSession _session = session;

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Top-level dispatcher must convert any failure into a protocol error."
    )]
    public DaemonResponse Dispatch(DaemonRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return Execute(request);
        }
        catch (AutomationException ex)
        {
            return DaemonResponse.Failure(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return DaemonResponse.Failure(ErrorCodes.InternalError, ex.Message);
        }
    }

    private static DaemonResponse Ack(string detail) =>
        DaemonResponse.Success(new AckPayload { Detail = detail });

    private static TimeSpan Timeout(int timeoutMs) =>
        timeoutMs <= 0
            ? throw new AutomationException(ErrorCodes.BadRequest, "Timeout must be positive.")
            : TimeSpan.FromMilliseconds(timeoutMs);

    private static string NormalizeRef(string input) =>
        ElementRef.TryNormalize(input, out var normalized)
            ? normalized
            : throw new AutomationException(
                ErrorCodes.BadRequest,
                $"'{input}' is not a valid element ref. Expected the form '@e5'."
            );

    private static string? NormalizeOptionalRef(string? input) =>
        input is null ? null : NormalizeRef(input);

    private static ElementSelector NormalizeSelector(ElementSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return !selector.IsEmpty
            ? selector with
            {
                ScopeRef = NormalizeOptionalRef(selector.ScopeRef),
            }
            : throw new AutomationException(
                ErrorCodes.BadRequest,
                "A selector requires --automation-id, --name, --name-contains, or --role."
            );
    }

    private static ElementTarget CreateTarget(string? elementRef, ElementSelector? selector) =>
        CreateOptionalTarget(elementRef, selector)
        ?? throw new AutomationException(
            ErrorCodes.BadRequest,
            "The command requires exactly one element ref or selector target."
        );

    private static ElementTarget? CreateOptionalTarget(
        string? elementRef,
        ElementSelector? selector
    ) =>
        (elementRef, selector) switch
        {
            ({ } value, null) => ElementTarget.ForRef(NormalizeRef(value)),
            (null, { } value) => ElementTarget.ForSelector(NormalizeSelector(value)),
            (null, null) => null,
            _ => throw new AutomationException(
                ErrorCodes.BadRequest,
                "Specify an element ref or selector, not both."
            ),
        };

    private static void ValidateWindowAction(WindowActionRequest request)
    {
        if (request.Action == WindowActionKind.Move && (request.X is null || request.Y is null))
        {
            throw new AutomationException(
                ErrorCodes.BadRequest,
                "window move requires --x and --y."
            );
        }

        if (
            request.Action == WindowActionKind.Resize
            && (request.Width is null || request.Height is null)
        )
        {
            throw new AutomationException(
                ErrorCodes.BadRequest,
                "window resize requires --width and --height."
            );
        }
    }

    private DaemonResponse Execute(DaemonRequest request) =>
        request switch
        {
            ListWindowsRequest => DaemonResponse.Success(
                new WindowListPayload { Windows = _session.ListWindows() }
            ),
            LaunchRequest r => DaemonResponse.Success(
                new WindowPayload
                {
                    Window = _session.Launch(r.Path, r.Arguments, Timeout(r.TimeoutMs)),
                }
            ),
            AttachRequest r => ExecuteAttach(r),
            SnapshotRequest r => ExecuteSnapshot(r),
            FindRequest r => ExecuteFind(r),
            ActivateRequest r => ExecuteActivate(r),
            ClickRequest r => ExecuteClick(r),
            FillRequest r => ExecuteFill(r),
            PressRequest r => ExecutePress(r),
            SelectRequest r => ExecuteSelect(r),
            ExpandRequest r => ExecuteExpand(r),
            ToggleRequest r => ExecuteToggle(r),
            ScrollRequest r => ExecuteScroll(r),
            WaitRequest r => ExecuteWait(r),
            ScreenshotRequest r => ExecuteScreenshot(r),
            WindowActionRequest r => ExecuteWindowAction(r),
            CloseRequest r => ExecuteClose(r),
            StatusRequest => DaemonResponse.Success(
                new StatusPayload { Status = _session.GetStatus() }
            ),
            ShutdownRequest => DaemonResponse.Success(new AckPayload { Detail = "shutting down" }),
            _ => DaemonResponse.Failure(
                ErrorCodes.BadRequest,
                $"Unsupported request type '{request.GetType().Name}'."
            ),
        };

    private DaemonResponse ExecuteAttach(AttachRequest request) =>
        request.Title is null && request.ProcessId is null && request.WindowHandle is null
            ? throw new AutomationException(
                ErrorCodes.BadRequest,
                "attach requires --window <title>, --pid, or --hwnd."
            )
            : DaemonResponse.Success(
                new WindowPayload
                {
                    Window = _session.Attach(
                        request.Title,
                        request.ProcessId,
                        request.WindowHandle
                    ),
                }
            );

    private DaemonResponse ExecuteScreenshot(ScreenshotRequest request) =>
        !Path.IsPathRooted(request.OutputPath)
            ? throw new AutomationException(
                ErrorCodes.BadRequest,
                "screenshot requires an absolute output path (the CLI resolves relative paths "
                    + "against the caller's directory; the daemon's differs)."
            )
            : DaemonResponse.Success(
                new ScreenshotPayload
                {
                    Path = _session.CaptureScreenshot(
                        NormalizeOptionalRef(request.Ref),
                        request.OutputPath
                    ),
                }
            );

    private DaemonResponse ExecuteWindowAction(WindowActionRequest request)
    {
        ValidateWindowAction(request);
        return DaemonResponse.Success(
            new WindowPayload
            {
                Window = _session.PerformWindowAction(
                    request.Action,
                    request.X,
                    request.Y,
                    request.Width,
                    request.Height
                ),
            }
        );
    }

    private DaemonResponse ExecuteSnapshot(SnapshotRequest request)
    {
        var options = new SnapshotOptions
        {
            InteractiveOnly = request.InteractiveOnly,
            MaxDepth = request.MaxDepth,
            View = request.View,
            ScopeRef = NormalizeOptionalRef(request.ScopeRef),
        };
        var result = _session.CaptureSnapshot(options);
        return DaemonResponse.Success(
            new SnapshotPayload { Root = result.Root, Generation = result.Generation }
        );
    }

    private DaemonResponse ExecuteFind(FindRequest request)
    {
        var result = _session.Find(NormalizeSelector(request.Selector), request.All);
        return DaemonResponse.Success(new FindPayload { Matches = result.Matches });
    }

    private DaemonResponse ExecuteClick(ClickRequest request)
    {
        var target = request switch
        {
            { Ref: { } elementRef, Selector: null, X: null, Y: null } => ClickTarget.ForRef(
                NormalizeRef(elementRef)
            ),
            { Ref: null, Selector: { } selector, X: null, Y: null } => ClickTarget.ForSelector(
                NormalizeSelector(selector)
            ),
            { Ref: null, Selector: null, X: { } x, Y: { } y } => ClickTarget.ForPoint(x, y),
            _ => throw new AutomationException(
                ErrorCodes.BadRequest,
                "click requires exactly one ref, selector, or --at x,y coordinate target."
            ),
        };

        _session.Click(target, request.Button, request.DoubleClick, Timeout(request.TimeoutMs));
        return Ack("clicked");
    }

    private DaemonResponse ExecuteActivate(ActivateRequest request)
    {
        _session.Activate(CreateTarget(request.Ref, request.Selector), Timeout(request.TimeoutMs));
        return Ack("activated");
    }

    private DaemonResponse ExecuteFill(FillRequest request)
    {
        _session.Fill(
            CreateTarget(request.Ref, request.Selector),
            request.Text,
            Timeout(request.TimeoutMs)
        );
        return Ack("filled");
    }

    private DaemonResponse ExecutePress(PressRequest request)
    {
        _session.Press(KeyGesture.Parse(request.Keys));
        return Ack($"pressed {request.Keys}");
    }

    private DaemonResponse ExecuteSelect(SelectRequest request)
    {
        _session.SelectItem(
            CreateTarget(request.Ref, request.Selector),
            request.Item,
            Timeout(request.TimeoutMs)
        );
        return Ack($"selected {request.Item}");
    }

    private DaemonResponse ExecuteExpand(ExpandRequest request)
    {
        _session.Expand(
            CreateTarget(request.Ref, request.Selector),
            request.Collapse,
            Timeout(request.TimeoutMs)
        );
        return Ack(request.Collapse ? "collapsed" : "expanded");
    }

    private DaemonResponse ExecuteToggle(ToggleRequest request)
    {
        _session.Toggle(
            CreateTarget(request.Ref, request.Selector),
            request.State,
            Timeout(request.TimeoutMs)
        );
        return Ack("toggled");
    }

    private DaemonResponse ExecuteScroll(ScrollRequest request)
    {
        _session.Scroll(
            CreateOptionalTarget(request.Ref, request.Selector),
            request.Direction,
            request.Amount,
            Timeout(request.TimeoutMs)
        );
        return Ack("scrolled");
    }

    private DaemonResponse ExecuteWait(WaitRequest request)
    {
        if (request.Ref is null && request.Selector is null && request.Text is null)
        {
            throw new AutomationException(
                ErrorCodes.BadRequest,
                "wait requires an element ref or --text."
            );
        }

        _session.WaitFor(
            CreateOptionalTarget(request.Ref, request.Selector),
            request.Text,
            request.Gone,
            Timeout(request.TimeoutMs)
        );
        return Ack("condition met");
    }

    private DaemonResponse ExecuteClose(CloseRequest request)
    {
        _session.CloseTarget(request.Force);
        return Ack("closed");
    }
}

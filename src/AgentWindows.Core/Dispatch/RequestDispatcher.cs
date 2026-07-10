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

    private static DaemonResponse UnsupportedRequest(DaemonRequest request) =>
        DaemonResponse.Failure(
            ErrorCodes.BadRequest,
            $"Unsupported request type '{request.GetType().Name}'."
        );

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
            request.Action != WindowActionKind.Resize
            || (request.Width is not null && request.Height is not null)
        )
        {
            return;
        }

        throw new AutomationException(
            ErrorCodes.BadRequest,
            "window resize requires --width and --height."
        );
    }

    private DaemonResponse Execute(DaemonRequest request) =>
        ExecuteSessionRequest(request)
        ?? ExecuteCaptureRequest(request)
        ?? ExecuteElementRequest(request)
        ?? ExecuteWindowRequest(request)
        ?? UnsupportedRequest(request);

    private DaemonResponse? ExecuteSessionRequest(DaemonRequest request) =>
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
            StatusRequest => DaemonResponse.Success(
                new StatusPayload { Status = _session.GetStatus() }
            ),
            ShutdownRequest => DaemonResponse.Success(new AckPayload { Detail = "shutting down" }),
            _ => null,
        };

    private DaemonResponse? ExecuteCaptureRequest(DaemonRequest request) =>
        request switch
        {
            SnapshotRequest r => ExecuteSnapshot(r),
            ScreenshotRequest r => ExecuteScreenshot(r),
            _ => null,
        };

    private DaemonResponse? ExecuteElementRequest(DaemonRequest request) =>
        request switch
        {
            ClickRequest r => ExecuteClick(r),
            FillRequest r => ExecuteFill(r),
            PressRequest r => ExecutePress(r),
            SelectRequest r => ExecuteSelect(r),
            ExpandRequest r => ExecuteExpand(r),
            ToggleRequest r => ExecuteToggle(r),
            ScrollRequest r => ExecuteScroll(r),
            WaitRequest r => ExecuteWait(r),
            _ => null,
        };

    private DaemonResponse? ExecuteWindowRequest(DaemonRequest request) =>
        request switch
        {
            WindowActionRequest r => ExecuteWindowAction(r),
            CloseRequest r => ExecuteClose(r),
            _ => null,
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
            ScopeRef = NormalizeOptionalRef(request.ScopeRef),
        };
        var result = _session.CaptureSnapshot(options);
        return DaemonResponse.Success(
            new SnapshotPayload { Root = result.Root, Generation = result.Generation }
        );
    }

    private DaemonResponse ExecuteClick(ClickRequest request)
    {
        var target = request switch
        {
            { Ref: { } elementRef } => ClickTarget.ForRef(NormalizeRef(elementRef)),
            { X: { } x, Y: { } y } => ClickTarget.ForPoint(x, y),
            _ => throw new AutomationException(
                ErrorCodes.BadRequest,
                "click requires an element ref or --at x,y coordinates."
            ),
        };

        _session.Click(target, request.Button, request.DoubleClick, Timeout(request.TimeoutMs));
        return Ack("clicked");
    }

    private DaemonResponse ExecuteFill(FillRequest request)
    {
        _session.Fill(NormalizeRef(request.Ref), request.Text, Timeout(request.TimeoutMs));
        return Ack("filled");
    }

    private DaemonResponse ExecutePress(PressRequest request)
    {
        _session.Press(KeyChord.Parse(request.Keys));
        return Ack($"pressed {request.Keys}");
    }

    private DaemonResponse ExecuteSelect(SelectRequest request)
    {
        _session.SelectItem(NormalizeRef(request.Ref), request.Item, Timeout(request.TimeoutMs));
        return Ack($"selected {request.Item}");
    }

    private DaemonResponse ExecuteExpand(ExpandRequest request)
    {
        _session.Expand(NormalizeRef(request.Ref), request.Collapse, Timeout(request.TimeoutMs));
        return Ack(request.Collapse ? "collapsed" : "expanded");
    }

    private DaemonResponse ExecuteToggle(ToggleRequest request)
    {
        _session.Toggle(NormalizeRef(request.Ref), request.State, Timeout(request.TimeoutMs));
        return Ack("toggled");
    }

    private DaemonResponse ExecuteScroll(ScrollRequest request)
    {
        _session.Scroll(
            NormalizeOptionalRef(request.Ref),
            request.Direction,
            request.Amount,
            Timeout(request.TimeoutMs)
        );
        return Ack("scrolled");
    }

    private DaemonResponse ExecuteWait(WaitRequest request)
    {
        if (request.Ref is null && request.Text is null)
        {
            throw new AutomationException(
                ErrorCodes.BadRequest,
                "wait requires an element ref or --text."
            );
        }

        _session.WaitFor(
            NormalizeOptionalRef(request.Ref),
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

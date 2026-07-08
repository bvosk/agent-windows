using System.Diagnostics.CodeAnalysis;
using AgentWindows.Core.Input;
using AgentWindows.Core.Model;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;
using AgentWindows.Core.Snapshot;

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
            AttachRequest r => DaemonResponse.Success(
                new WindowPayload { Window = _session.Attach(r.Title, r.ProcessId, r.WindowHandle) }
            ),
            SnapshotRequest r => ExecuteSnapshot(r),
            ClickRequest r => ExecuteClick(r),
            FillRequest r => ExecuteFill(r),
            PressRequest r => ExecutePress(r),
            SelectRequest r => ExecuteSelect(r),
            ExpandRequest r => ExecuteExpand(r),
            ToggleRequest r => ExecuteToggle(r),
            ScrollRequest r => ExecuteScroll(r),
            WaitRequest r => ExecuteWait(r),
            ScreenshotRequest r => DaemonResponse.Success(
                new ScreenshotPayload
                {
                    Path = _session.CaptureScreenshot(NormalizeOptionalRef(r.Ref), r.OutputPath),
                }
            ),
            WindowActionRequest r => DaemonResponse.Success(
                new WindowPayload
                {
                    Window = _session.PerformWindowAction(r.Action, r.X, r.Y, r.Width, r.Height),
                }
            ),
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
        _session.Press(KeyGesture.Parse(request.Keys));
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

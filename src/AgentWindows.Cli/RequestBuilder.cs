using System.Globalization;
using AgentWindows.Core.Model;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli;

/// <summary>Pure translation from parsed CLI input to protocol requests.</summary>
public static class RequestBuilder
{
    public static LaunchRequest BuildLaunch(string app, string? arguments, int timeoutMs) =>
        new()
        {
            Path = app,
            Arguments = arguments,
            TimeoutMs = timeoutMs,
        };

    public static AttachRequest BuildAttach(string? title, int? processId, long? windowHandle) =>
        title is null && processId is null && windowHandle is null
            ? throw new AutomationException(
                ErrorCodes.BadRequest,
                "attach requires --window <title>, --pid, or --hwnd."
            )
            : new AttachRequest
            {
                Title = title,
                ProcessId = processId,
                WindowHandle = windowHandle,
            };

    public static SnapshotRequest BuildSnapshot(
        bool interactiveOnly,
        int? maxDepth,
        string? scope
    ) =>
        new()
        {
            InteractiveOnly = interactiveOnly,
            MaxDepth = maxDepth,
            ScopeRef = scope,
        };

    public static ClickRequest BuildClick(
        string? elementRef,
        string? at,
        bool right,
        bool middle,
        bool doubleClick,
        int timeoutMs
    )
    {
        if (elementRef is null && at is null)
        {
            throw new AutomationException(
                ErrorCodes.BadRequest,
                "click requires an element ref (e.g. '@e5') or --at x,y."
            );
        }

        var point = at is null ? ((int X, int Y)?)null : ParsePoint(at);
        var button = MouseButtonKind.Left;
        if (right)
        {
            button = MouseButtonKind.Right;
        }
        else if (middle)
        {
            button = MouseButtonKind.Middle;
        }

        return new ClickRequest
        {
            Ref = elementRef,
            X = point?.X,
            Y = point?.Y,
            Button = button,
            DoubleClick = doubleClick,
            TimeoutMs = timeoutMs,
        };
    }

    public static ScrollRequest BuildScroll(
        string? elementRef,
        string direction,
        double amount,
        int timeoutMs
    ) =>
        new()
        {
            Ref = elementRef,
            Direction = ParseDirection(direction),
            Amount = amount,
            TimeoutMs = timeoutMs,
        };

    public static ToggleRequest BuildToggle(string elementRef, bool on, bool off, int timeoutMs)
    {
        if (on && off)
        {
            throw new AutomationException(
                ErrorCodes.BadRequest,
                "toggle accepts --on or --off, not both."
            );
        }

        bool? state = null;
        if (on)
        {
            state = true;
        }
        else if (off)
        {
            state = false;
        }

        return new ToggleRequest
        {
            Ref = elementRef,
            State = state,
            TimeoutMs = timeoutMs,
        };
    }

    public static WaitRequest BuildWait(string? elementRef, string? text, bool gone, int timeoutMs)
    {
        return elementRef is null && text is null
            ? throw new AutomationException(
                ErrorCodes.BadRequest,
                "wait requires an element ref (e.g. '@e5') or --text."
            )
            : new WaitRequest
            {
                Ref = elementRef,
                Text = text,
                Gone = gone,
                TimeoutMs = timeoutMs,
            };
    }

    public static WindowActionRequest BuildWindowAction(
        string action,
        int? x,
        int? y,
        int? width,
        int? height
    ) =>
        new()
        {
            Action = ParseWindowAction(action),
            X = x,
            Y = y,
            Width = width,
            Height = height,
        };

    public static (int X, int Y) ParsePoint(string at)
    {
        ArgumentNullException.ThrowIfNull(at);
        var parts = at.Split(',', StringSplitOptions.TrimEntries);
        return
            parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            ? (x, y)
            : throw new AutomationException(
                ErrorCodes.BadRequest,
                $"Cannot parse coordinates '{at}'. Expected the form 'x,y', e.g. '640,220'."
            );
    }

    public static ScrollDirection ParseDirection(string direction)
    {
        ArgumentNullException.ThrowIfNull(direction);
        return Enum.TryParse<ScrollDirection>(direction, ignoreCase: true, out var parsed)
            ? parsed
            : throw new AutomationException(
                ErrorCodes.BadRequest,
                $"Unknown scroll direction '{direction}'. Expected up, down, left, or right."
            );
    }

    public static WindowActionKind ParseWindowAction(string action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Enum.TryParse<WindowActionKind>(action, ignoreCase: true, out var parsed)
            ? parsed
            : throw new AutomationException(
                ErrorCodes.BadRequest,
                $"Unknown window action '{action}'. "
                    + "Expected focus, move, resize, maximize, minimize, or restore."
            );
    }
}

using System.Globalization;
using AgentWindows.Core.Input;
using AgentWindows.Core.Protocol.Interaction;
using AgentWindows.Core.Session;
using AgentWindows.Core.Windows;

namespace AgentWindows.Cli.ConsoleHost;

/// <summary>
/// CLI-specific parsing and translation (coordinates, flag pairs, enum names).
/// Request-shape validation lives in the dispatcher — the single authority that
/// also covers raw-JSON REPL input.
/// </summary>
public static class RequestBuilder
{
    public static ClickRequest BuildClick(
        string? elementRef,
        string? at,
        bool right,
        bool middle,
        bool doubleClick,
        int timeoutMs
    )
    {
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

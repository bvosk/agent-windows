using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using AgentWindows.Core.Input;
using AgentWindows.Core.Model;
using AgentWindows.Core.Session;
using AgentWindows.Core.Snapshot;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace AgentWindows.Automation;

public sealed class FlaUiSession : IAutomationSession
{
    private readonly UIA3Automation _automation = new();
    private Dictionary<string, AutomationElement> _refs = [];
    private Application? _app;
    private Window? _target;
    private int _generation;
    private int _nextRefIndex = 1;

    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var windows = FindDesktopWindows();
        return [.. windows.Select(ToWindowInfo)];
    }

    public WindowInfo Launch(string path, string? arguments, TimeSpan timeout)
    {
        Application app;
        try
        {
            app = Application.Launch(path, arguments ?? "");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new AutomationException(
                ErrorCodes.LaunchFailed,
                $"Failed to launch '{path}': {ex.Message}",
                ex
            );
        }

        Window? window;
        try
        {
            window = app.GetMainWindow(_automation, timeout);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new AutomationException(
                ErrorCodes.LaunchFailed,
                $"'{path}' started but its process exited or has no accessible main window "
                    + $"({ex.Message}). Packaged (Store/UWP) apps hand off to another process; "
                    + "use 'agent-windows attach --window <title>' instead.",
                ex
            );
        }

        if (window is null)
        {
            throw new AutomationException(
                ErrorCodes.LaunchFailed,
                $"'{path}' started (pid {app.ProcessId}) but no main window appeared within "
                    + $"{timeout.TotalSeconds:0}s. For UWP apps, attach by title instead."
            );
        }

        _app = app;
        SetTarget(window);
        return ToWindowInfo(window);
    }

    public WindowInfo Attach(string? title, int? processId, long? windowHandle)
    {
        var element = FindWindowElement(title, processId, windowHandle);
        var info = ToWindowInfo(element);
        if (info.IsElevated == true)
        {
            throw new AutomationException(
                ErrorCodes.ElevatedTarget,
                $"'{info.Title}' (pid {info.ProcessId}) runs elevated and this process does not. "
                    + "Re-run agent-windows from an elevated terminal to automate it."
            );
        }

        SetTarget(element.AsWindow());
        return info;
    }

    public SnapshotResult CaptureSnapshot(SnapshotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var root = options.ScopeRef is null ? RequireTarget() : Resolve(options.ScopeRef);
        var result = SnapshotBuilder.Build(root, options, _nextRefIndex);
        _refs = new Dictionary<string, AutomationElement>(result.Refs, StringComparer.Ordinal);
        _nextRefIndex = result.NextRefIndex;
        _generation++;
        return new SnapshotResult { Root = result.Root, Generation = _generation };
    }

    public void Click(
        ClickTarget target,
        MouseButtonKind button,
        bool doubleClick,
        TimeSpan timeout
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        var mouseButton = ToMouseButton(button);
        if (target.ElementRef is null)
        {
            Mouse.Position = new System.Drawing.Point(target.X ?? 0, target.Y ?? 0);
            ClickCurrentPosition(mouseButton, doubleClick);
            return;
        }

        var element = Resolve(target.ElementRef);
        WaitUntilActionable(element, target.ElementRef, timeout);
        Mouse.Position = GetClickablePoint(element, target.ElementRef);
        ClickCurrentPosition(mouseButton, doubleClick);
    }

    public void Fill(string elementRef, string text, TimeSpan timeout)
    {
        var element = Resolve(elementRef);
        WaitUntilActionable(element, elementRef, timeout);
        var valuePattern = element.Patterns.Value.PatternOrDefault;
        if (valuePattern is not null && !valuePattern.IsReadOnly.ValueOrDefault)
        {
            valuePattern.SetValue(text);
            return;
        }

        element.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(text);
    }

    public void Press(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        var modifiers = gesture.Modifiers.Select(KeyMapper.ToVirtualKey).ToArray();
        if (KeyMapper.TryMapKey(gesture.Key, out var key))
        {
            if (modifiers.Length > 0)
            {
                using (Keyboard.Pressing(modifiers))
                {
                    Keyboard.Type(key);
                }
            }
            else
            {
                Keyboard.Type(key);
            }

            return;
        }

        if (modifiers.Length == 0 && gesture.Key.Length == 1)
        {
            Keyboard.Type(gesture.Key);
            return;
        }

        throw new AutomationException(
            ErrorCodes.BadRequest,
            $"Key '{gesture.Key}' cannot be combined with modifiers."
        );
    }

    public void SelectItem(string elementRef, string item, TimeSpan timeout)
    {
        var element = Resolve(elementRef);
        WaitUntilActionable(element, elementRef, timeout);
        try
        {
            SelectItemCore(element, item);
        }
        catch (InvalidOperationException ex)
        {
            throw new AutomationException(
                ErrorCodes.NotFound,
                $"No item '{item}' found in {ElementRef.Display(elementRef)}: {ex.Message}",
                ex
            );
        }
    }

    public void Expand(string elementRef, bool collapse, TimeSpan timeout)
    {
        var element = Resolve(elementRef);
        WaitUntilActionable(element, elementRef, timeout);
        var pattern =
            element.Patterns.ExpandCollapse.PatternOrDefault
            ?? throw PatternUnsupported(elementRef, "ExpandCollapse");
        if (collapse)
        {
            pattern.Collapse();
        }
        else
        {
            pattern.Expand();
        }
    }

    public void Toggle(string elementRef, bool? desiredState, TimeSpan timeout)
    {
        var element = Resolve(elementRef);
        WaitUntilActionable(element, elementRef, timeout);
        var pattern =
            element.Patterns.Toggle.PatternOrDefault
            ?? throw PatternUnsupported(elementRef, "Toggle");
        var isOn = pattern.ToggleState.ValueOrDefault == ToggleState.On;
        if (desiredState is { } desired && desired == isOn)
        {
            return;
        }

        pattern.Toggle();
    }

    public void Scroll(
        string? elementRef,
        ScrollDirection direction,
        double amount,
        TimeSpan timeout
    )
    {
        var element = elementRef is null ? RequireTarget() : Resolve(elementRef);
        if (elementRef is not null)
        {
            WaitUntilActionable(element, elementRef, timeout);
        }

        if (TryPatternScroll(element, direction, amount))
        {
            return;
        }

        WheelScroll(element, direction, amount);
    }

    public void WaitFor(string? elementRef, string? text, bool untilGone, TimeSpan timeout)
    {
        if (elementRef is not null)
        {
            var element = Resolve(elementRef);
            var display = ElementRef.Display(elementRef);
            Poller.WaitUntil(
                () => IsActionable(element) != untilGone,
                timeout,
                untilGone
                    ? $"{display} was still present after {timeout.TotalSeconds:0}s."
                    : $"{display} did not become interactable within {timeout.TotalSeconds:0}s."
            );
            return;
        }

        var target = RequireTarget();
        Poller.WaitUntil(
            () => ContainsText(target, text!) != untilGone,
            timeout,
            untilGone
                ? $"Text '{text}' was still present after {timeout.TotalSeconds:0}s."
                : $"Text '{text}' did not appear within {timeout.TotalSeconds:0}s."
        );
    }

    public string CaptureScreenshot(string? elementRef, string outputPath)
    {
        var element = elementRef is not null ? Resolve(elementRef) : ScreenshotFallbackElement();
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var image = Capture.Element(element);
        image.ToFile(fullPath);
        return fullPath;
    }

    public WindowInfo PerformWindowAction(
        WindowActionKind kind,
        int? x,
        int? y,
        int? width,
        int? height
    )
    {
        var window = RequireTarget();
        if (kind == WindowActionKind.Focus)
        {
            window.SetForeground();
            window.Focus();
        }
        else if (kind == WindowActionKind.Move)
        {
            RequireTransform(window)
                .Move(RequireArg(x, "move requires --x"), RequireArg(y, "move requires --y"));
        }
        else if (kind == WindowActionKind.Resize)
        {
            RequireTransform(window)
                .Resize(
                    RequireArg(width, "resize requires --width"),
                    RequireArg(height, "resize requires --height")
                );
        }
        else
        {
            SetVisualState(window, ToVisualState(kind));
        }

        return ToWindowInfo(window);
    }

    public void CloseTarget(bool force)
    {
        var window = RequireTarget();
        if (force)
        {
            var processId = window.Properties.ProcessId.ValueOrDefault;
            using var process = Process.GetProcessById(processId);
            process.Kill();
        }
        else
        {
            window.Close();
        }

        _target = null;
        _refs.Clear();
    }

    public SessionStatus GetStatus() =>
        new()
        {
            DaemonProcessId = Environment.ProcessId,
            Target = _target is not null && IsAvailable(_target) ? ToWindowInfo(_target) : null,
            SnapshotGeneration = _generation,
            RefCount = _refs.Count,
        };

    public void Dispose()
    {
        _app?.Dispose();
        _automation.Dispose();
    }

    private static void ClickCurrentPosition(MouseButton button, bool doubleClick)
    {
        if (doubleClick)
        {
            Mouse.DoubleClick(button);
        }
        else
        {
            Mouse.Click(button);
        }
    }

    private static MouseButton ToMouseButton(MouseButtonKind kind) =>
        kind switch
        {
            MouseButtonKind.Left => MouseButton.Left,
            MouseButtonKind.Right => MouseButton.Right,
            MouseButtonKind.Middle => MouseButton.Middle,
            _ => MouseButton.Left,
        };

    private static void SelectItemCore(AutomationElement element, string item)
    {
        var controlType = element.Properties.ControlType.ValueOrDefault;
        if (controlType == ControlType.ComboBox)
        {
            element.AsComboBox().Select(item);
        }
        else if (controlType == ControlType.List)
        {
            element.AsListBox().Select(item);
        }
        else if (controlType == ControlType.Tab)
        {
            element.AsTab().SelectTabItem(item);
        }
        else
        {
            SelectDescendantByName(element, item);
        }
    }

    private static void SelectDescendantByName(AutomationElement element, string item)
    {
        var match =
            element.FindFirstDescendant(cf => cf.ByName(item))
            ?? throw new AutomationException(
                ErrorCodes.NotFound,
                $"No descendant named '{item}' found."
            );
        var pattern =
            match.Patterns.SelectionItem.PatternOrDefault
            ?? throw new AutomationException(
                ErrorCodes.PatternUnsupported,
                $"'{item}' does not support selection."
            );
        pattern.Select();
    }

    private static bool TryPatternScroll(
        AutomationElement element,
        ScrollDirection direction,
        double amount
    )
    {
        var pattern = element.Patterns.Scroll.PatternOrDefault;
        if (pattern is null)
        {
            return false;
        }

        var vertical = direction is ScrollDirection.Up or ScrollDirection.Down;
        var scrollable = vertical
            ? pattern.VerticallyScrollable.ValueOrDefault
            : pattern.HorizontallyScrollable.ValueOrDefault;
        if (!scrollable)
        {
            return false;
        }

        var scrollAmount = direction switch
        {
            ScrollDirection.Up => ScrollAmount.SmallDecrement,
            ScrollDirection.Left => ScrollAmount.SmallDecrement,
            ScrollDirection.Down => ScrollAmount.SmallIncrement,
            ScrollDirection.Right => ScrollAmount.SmallIncrement,
            _ => ScrollAmount.SmallIncrement,
        };
        var steps = Math.Max(1, (int)Math.Round(amount));
        for (var i = 0; i < steps; i++)
        {
            if (vertical)
            {
                pattern.Scroll(ScrollAmount.NoAmount, scrollAmount);
            }
            else
            {
                pattern.Scroll(scrollAmount, ScrollAmount.NoAmount);
            }
        }

        return true;
    }

    private static void WheelScroll(
        AutomationElement element,
        ScrollDirection direction,
        double amount
    )
    {
        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        if (!rect.IsEmpty)
        {
            Mouse.Position = new System.Drawing.Point(
                rect.X + (rect.Width / 2),
                rect.Y + (rect.Height / 2)
            );
        }

        if (direction == ScrollDirection.Up)
        {
            Mouse.Scroll(amount);
        }
        else if (direction == ScrollDirection.Down)
        {
            Mouse.Scroll(-amount);
        }
        else if (direction == ScrollDirection.Left)
        {
            Mouse.HorizontalScroll(-amount);
        }
        else
        {
            Mouse.HorizontalScroll(amount);
        }
    }

    private static bool ContainsText(AutomationElement root, string text)
    {
        try
        {
            var descendants = root.FindAllDescendants();
            return Array.Exists(
                descendants,
                d =>
                    d.Properties.Name.ValueOrDefault?.Contains(
                        text,
                        StringComparison.OrdinalIgnoreCase
                    ) == true
            );
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool IsAvailable(AutomationElement element)
    {
        try
        {
            _ = element.Properties.ProcessId.ValueOrDefault;
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool IsActionable(AutomationElement element)
    {
        try
        {
            return element.Properties.IsEnabled.ValueOrDefault
                && !element.Properties.IsOffscreen.ValueOrDefault;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static void WaitUntilActionable(
        AutomationElement element,
        string elementRef,
        TimeSpan timeout
    ) =>
        Poller.WaitUntil(
            () => IsActionable(element),
            timeout,
            $"{ElementRef.Display(elementRef)} did not become enabled and on-screen within "
                + $"{timeout.TotalSeconds:0}s."
        );

    private static System.Drawing.Point GetClickablePoint(
        AutomationElement element,
        string elementRef
    )
    {
        if (element.TryGetClickablePoint(out var point))
        {
            return point;
        }

        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        return rect.IsEmpty
            ? throw new AutomationException(
                ErrorCodes.InternalError,
                $"{ElementRef.Display(elementRef)} has no clickable point or bounds."
            )
            : new System.Drawing.Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
    }

    private static AutomationException PatternUnsupported(string elementRef, string pattern) =>
        new(
            ErrorCodes.PatternUnsupported,
            $"{ElementRef.Display(elementRef)} does not support the {pattern} pattern."
        );

    private static int RequireArg(int? value, string message) =>
        value ?? throw new AutomationException(ErrorCodes.BadRequest, message);

    private static FlaUI.Core.Patterns.ITransformPattern RequireTransform(Window window) =>
        window.Patterns.Transform.PatternOrDefault
        ?? throw new AutomationException(
            ErrorCodes.PatternUnsupported,
            "The target window does not support move/resize."
        );

    private static WindowVisualState ToVisualState(WindowActionKind kind) =>
        kind switch
        {
            WindowActionKind.Maximize => WindowVisualState.Maximized,
            WindowActionKind.Minimize => WindowVisualState.Minimized,
            WindowActionKind.Focus => WindowVisualState.Normal,
            WindowActionKind.Move => WindowVisualState.Normal,
            WindowActionKind.Resize => WindowVisualState.Normal,
            WindowActionKind.Restore => WindowVisualState.Normal,
            _ => WindowVisualState.Normal,
        };

    private static void SetVisualState(Window window, WindowVisualState state)
    {
        var pattern =
            window.Patterns.Window.PatternOrDefault
            ?? throw new AutomationException(
                ErrorCodes.PatternUnsupported,
                "The target window does not support minimize/maximize/restore."
            );
        pattern.SetWindowVisualState(state);
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    private static AutomationElement EnsureAvailable(
        AutomationElement element,
        string elementRef
    ) =>
        IsAvailable(element)
            ? element
            : throw new AutomationException(
                ErrorCodes.StaleRef,
                $"{ElementRef.Display(elementRef)} no longer exists in the UI. "
                    + "Take a new snapshot."
            );

    private static AutomationElement FindByProcessId(AutomationElement[] windows, int pid) =>
        Array.Find(windows, w => w.Properties.ProcessId.ValueOrDefault == pid)
        ?? throw new AutomationException(
            ErrorCodes.NotFound,
            $"No top-level window found for pid {pid}."
        );

    private static AutomationElement FindByTitle(AutomationElement[] windows, string? title) =>
        title is null
            ? throw new AutomationException(
                ErrorCodes.BadRequest,
                "attach requires --window <title>, --pid, or --hwnd."
            )
            : Array.Find(
                windows,
                w =>
                    w.Properties.Name.ValueOrDefault?.Contains(
                        title,
                        StringComparison.OrdinalIgnoreCase
                    ) == true
            )
                ?? throw new AutomationException(
                    ErrorCodes.NotFound,
                    $"No window with a title containing '{title}' found. "
                        + "Run 'agent-windows list' to see available windows."
                );

    private static WindowInfo ToWindowInfo(AutomationElement element)
    {
        var processId = element.Properties.ProcessId.ValueOrDefault;
        var handle = element.Properties.NativeWindowHandle.ValueOrDefault;
        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        return new WindowInfo
        {
            Title = element.Properties.Name.ValueOrDefault ?? "",
            WindowHandle = handle.ToInt64(),
            ProcessId = processId,
            ProcessName = GetProcessName(processId),
            IsElevated = ElevationDetector.ProcessIsElevated(processId),
            Bounds = rect.IsEmpty
                ? null
                : new BoundingRect(rect.X, rect.Y, rect.Width, rect.Height),
        };
    }

    private AutomationElement[] FindDesktopWindows() =>
        _automation.GetDesktop().FindAllChildren(cf => cf.ByControlType(ControlType.Window));

    private AutomationElement FindWindowElement(string? title, int? processId, long? windowHandle)
    {
        if (windowHandle is { } handle)
        {
            return _automation.FromHandle(new IntPtr(handle));
        }

        var windows = FindDesktopWindows();
        return processId is { } pid ? FindByProcessId(windows, pid) : FindByTitle(windows, title);
    }

    private void SetTarget(Window window)
    {
        _target = window;
        _refs.Clear();
    }

    private Window RequireTarget()
    {
        var target =
            _target
            ?? throw new AutomationException(
                ErrorCodes.NoTarget,
                "No target window. Run 'agent-windows attach --window <title>' or "
                    + "'agent-windows launch --app <path>' first."
            );
        return IsAvailable(target)
            ? target
            : throw new AutomationException(
                ErrorCodes.NoTarget,
                "The target window no longer exists. Attach to another window."
            );
    }

    private AutomationElement ScreenshotFallbackElement() =>
        _target is not null && IsAvailable(_target) ? _target : _automation.GetDesktop();

    private AutomationElement Resolve(string elementRef) =>
        !_refs.TryGetValue(elementRef, out var element)
            ? throw MissingRefError(elementRef)
            : EnsureAvailable(element, elementRef);

    private AutomationException MissingRefError(string elementRef)
    {
        var index = int.Parse(elementRef[1..], CultureInfo.InvariantCulture);
        var code = index < _nextRefIndex ? ErrorCodes.StaleRef : ErrorCodes.UnknownRef;
        return new AutomationException(
            code,
            $"{ElementRef.Display(elementRef)} is not part of the most recent snapshot "
                + $"(generation {_generation}). Run 'agent-windows snapshot' again."
        );
    }
}

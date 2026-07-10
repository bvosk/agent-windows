using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AgentWindows.Automation.Input;
using AgentWindows.Automation.Snapshots;
using AgentWindows.Automation.Windows;
using AgentWindows.Core.Elements;
using AgentWindows.Core.Input;
using AgentWindows.Core.Session;
using AgentWindows.Core.Snapshots;
using AgentWindows.Core.Windows;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace AgentWindows.Automation.Session;

public sealed class FlaUiSession : IAutomationSession
{
    private readonly UIA3Automation _automation = new();
    private Dictionary<string, AutomationElement> _refs = [];
    private Application? _app;
    private Window? _target;
    private int _generation;
    private int _nextRefIndex = 1;

    public IReadOnlyList<WindowInfo> ListWindows() => Win32WindowEnumerator.ListTopLevelWindows();

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
        var info = FindWindow(title, processId, windowHandle);
        if (info.IsElevated == true && !ProcessInterop.CurrentProcessIsElevated())
        {
            throw new AutomationException(
                ErrorCodes.ElevatedTarget,
                $"'{info.Title}' (pid {info.ProcessId}) runs elevated and this process does not. "
                    + "Re-run agent-windows from an elevated terminal to automate it."
            );
        }

        var element = _automation.FromHandle(new IntPtr(info.WindowHandle));
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
        if (target.ElementRef is null)
        {
            FlaUiElementActions.ClickAt(target.X ?? 0, target.Y ?? 0, button, doubleClick);
            return;
        }

        var element = Resolve(target.ElementRef);
        FlaUiElementActions.Click(element, target.ElementRef, button, doubleClick, timeout);
    }

    public void Fill(string elementRef, string text, TimeSpan timeout)
    {
        var element = Resolve(elementRef);
        FlaUiElementActions.Fill(element, elementRef, text, timeout);
    }

    public void Press(KeyChord chord) => FlaUiElementActions.Press(chord);

    public void SelectItem(string elementRef, string item, TimeSpan timeout)
    {
        var element = Resolve(elementRef);
        FlaUiElementActions.SelectItem(element, elementRef, item, timeout);
    }

    public void Expand(string elementRef, bool collapse, TimeSpan timeout)
    {
        var element = Resolve(elementRef);
        FlaUiElementActions.Expand(element, elementRef, collapse, timeout);
    }

    public void Toggle(string elementRef, bool? desiredState, TimeSpan timeout)
    {
        var element = Resolve(elementRef);
        FlaUiElementActions.Toggle(element, elementRef, desiredState, timeout);
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
            FlaUiElementActions.WaitUntilActionable(element, elementRef, timeout);
        }

        FlaUiElementActions.Scroll(element, direction, amount);
    }

    public void WaitFor(string? elementRef, string? text, bool untilGone, TimeSpan timeout)
    {
        if (elementRef is not null)
        {
            var element = Resolve(elementRef);
            FlaUiElementActions.WaitForElement(element, elementRef, untilGone, timeout);
            return;
        }

        if (text is null)
        {
            throw new AutomationException(
                ErrorCodes.BadRequest,
                "Wait requires either an element ref or text."
            );
        }

        FlaUiElementActions.WaitForText(RequireTarget(), text, untilGone, timeout);
    }

    public string CaptureScreenshot(string? elementRef, string outputPath)
    {
        var element = elementRef is not null ? Resolve(elementRef) : ScreenshotFallbackElement();
        // The dispatcher rejects relative paths; outputPath is absolute here.
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var image = Capture.Element(element);
        image.ToFile(outputPath);
        return outputPath;
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
            // Argument presence is validated by the dispatcher.
            RequireTransform(window).Move(x.GetValueOrDefault(), y.GetValueOrDefault());
        }
        else if (kind == WindowActionKind.Resize)
        {
            RequireTransform(window).Resize(width.GetValueOrDefault(), height.GetValueOrDefault());
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

    /// <summary>
    /// Resolves the attach target against the same Win32 enumeration `list` uses,
    /// so anything `list` reports is attachable.
    /// </summary>
    private static WindowInfo FindWindow(string? title, int? processId, long? windowHandle)
    {
        var windows = Win32WindowEnumerator.ListTopLevelWindows();
        return (windowHandle, processId, title) switch
        {
            ({ } handle, _, _) => windows.FirstOrDefault(w => w.WindowHandle == handle)
                ?? throw new AutomationException(
                    ErrorCodes.NotFound,
                    $"No visible top-level window with handle 0x{handle:X} found."
                ),
            (_, { } pid, _) => windows.FirstOrDefault(w => w.ProcessId == pid)
                ?? throw new AutomationException(
                    ErrorCodes.NotFound,
                    $"No top-level window found for pid {pid}."
                ),
            (_, _, { } text) => windows.FirstOrDefault(w =>
                w.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
            )
                ?? throw new AutomationException(
                    ErrorCodes.NotFound,
                    $"No window with a title containing '{text}' found. "
                        + "Run 'agent-windows list' to see available windows."
                ),
            // The dispatcher rejects attach requests with no selector.
            _ => throw new UnreachableException(),
        };
    }

    private static WindowInfo ToWindowInfo(AutomationElement element)
    {
        var processId = element.Properties.ProcessId.ValueOrDefault;
        var (processName, isElevated) = ProcessInterop.GetProcessInfo(processId);
        var handle = element.Properties.NativeWindowHandle.ValueOrDefault;
        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        return new WindowInfo
        {
            Title = element.Properties.Name.ValueOrDefault ?? "",
            WindowHandle = handle.ToInt64(),
            ProcessId = processId,
            ProcessName = processName,
            IsElevated = isElevated,
            Bounds = RectConversions.ToBoundingRect(rect),
        };
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
        var code =
            ElementRef.TryGetIndex(elementRef, out var index) && index < _nextRefIndex
                ? ErrorCodes.StaleRef
                : ErrorCodes.UnknownRef;
        return new AutomationException(
            code,
            $"{ElementRef.Display(elementRef)} is not part of the most recent snapshot "
                + $"(generation {_generation}). Run 'agent-windows snapshot' again."
        );
    }
}

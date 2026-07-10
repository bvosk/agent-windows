using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.Core.Identifiers;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace AgentWindows.Automation.Session;

[SuppressMessage(
    "Maintainability",
    "CA1506:Avoid excessive class coupling",
    Justification = "The UIA session intentionally coordinates protocol, FlaUI, input, and window types."
)]
public sealed class FlaUiSession : IAutomationSession
{
    private readonly UIA3Automation _automation = new();
    private Dictionary<string, AutomationElement> _refs = [];
    private Application? _app;
    private Window? _target;
    private WindowInfo? _targetInfo;
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
        var info = ToWindowInfo(window);
        SetTarget(window, info);
        return info;
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
        SetTarget(element.AsWindow(), info);
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

    public FindResult Find(ElementSelector selector, bool all)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var matches = FindElements(selector, all || selector.RequireUnique);
        var error = matches.Length switch
        {
            0 => SelectorNotFound(selector),
            _ when selector.RequireUnique && matches.Length != 1 => new AutomationException(
                ErrorCodes.Ambiguous,
                $"Selector matched {matches.Length} elements; refine it or omit --require-unique."
            ),
            _ => null,
        };

        return error is null
            ? new FindResult { Matches = matches.Select(CreateFindNode).ToArray() }
            : throw error;
    }

    public void Activate(ElementTarget target, TimeSpan timeout)
    {
        var element = Resolve(target);
        if (element.Patterns.Invoke.PatternOrDefault is { } invoke)
        {
            invoke.Invoke();
            return;
        }

        if (element.Patterns.Toggle.PatternOrDefault is { } toggle)
        {
            toggle.Toggle();
            return;
        }

        if (element.Patterns.SelectionItem.PatternOrDefault is { } selectionItem)
        {
            selectionItem.Select();
            return;
        }

        if (element.Patterns.ExpandCollapse.PatternOrDefault is { } expandCollapse)
        {
            if (expandCollapse.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Collapsed)
            {
                expandCollapse.Expand();
            }
            else
            {
                expandCollapse.Collapse();
            }

            return;
        }

        throw PatternUnsupported(
            Display(target),
            "Invoke, Toggle, SelectionItem, or ExpandCollapse"
        );
    }

    public void Click(
        ClickTarget target,
        MouseButtonKind button,
        bool doubleClick,
        TimeSpan timeout
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ElementRef is null && target.Selector is null)
        {
            NativeInput.Click(
                new System.Drawing.Point(target.X ?? 0, target.Y ?? 0),
                button,
                doubleClick
            );
            return;
        }

        var elementTarget = target.ElementRef is not null
            ? ElementTarget.ForRef(target.ElementRef)
            : ElementTarget.ForSelector(target.Selector!);
        var element = Resolve(elementTarget);
        var display = Display(elementTarget);
        WaitUntilActionable(element, display, timeout);
        NativeInput.Click(GetClickablePoint(element, display), button, doubleClick);
    }

    public void Fill(ElementTarget target, string text, TimeSpan timeout)
    {
        var element = Resolve(target);
        var valuePattern = element.Patterns.Value.PatternOrDefault;
        if (valuePattern is not null && !valuePattern.IsReadOnly.ValueOrDefault)
        {
            valuePattern.SetValue(text);
            return;
        }

        element.Focus();
        NativeInput.Press(KeyChord.Parse("Ctrl+A"));
        NativeInput.TypeText(text);
    }

    public void Press(KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        try
        {
            NativeInput.Press(chord);
        }
        catch (ArgumentException ex)
        {
            throw new AutomationException(
                ErrorCodes.BadRequest,
                $"Unknown key '{chord.Key}'. Use a named key (e.g. Enter, F5, PageDown) "
                    + "or a single character.",
                ex
            );
        }
    }

    public void SelectItem(ElementTarget target, string item, TimeSpan timeout)
    {
        var element = Resolve(target);
        var display = Display(target);
        try
        {
            SelectItemCore(element, item, timeout);
        }
        catch (InvalidOperationException ex)
        {
            throw new AutomationException(
                ErrorCodes.NotFound,
                $"No item '{item}' found in {display}: {ex.Message}",
                ex
            );
        }
    }

    public void Expand(ElementTarget target, bool collapse, TimeSpan timeout)
    {
        var element = Resolve(target);
        var display = Display(target);
        var pattern =
            element.Patterns.ExpandCollapse.PatternOrDefault
            ?? throw PatternUnsupported(display, "ExpandCollapse");
        if (collapse)
        {
            pattern.Collapse();
        }
        else
        {
            pattern.Expand();
        }
    }

    public void Toggle(ElementTarget target, bool? desiredState, TimeSpan timeout)
    {
        var element = Resolve(target);
        var display = Display(target);
        var pattern =
            element.Patterns.Toggle.PatternOrDefault ?? throw PatternUnsupported(display, "Toggle");
        var isOn = pattern.ToggleState.ValueOrDefault == ToggleState.On;
        if (desiredState is { } desired && desired == isOn)
        {
            return;
        }

        pattern.Toggle();
    }

    public void Scroll(
        ElementTarget? target,
        ScrollDirection direction,
        double amount,
        TimeSpan timeout
    )
    {
        var element = target is null ? RequireTarget() : Resolve(target);
        if (target is not null)
        {
            WaitUntilActionable(element, Display(target), timeout);
        }

        if (TryPatternScroll(element, direction, amount))
        {
            return;
        }

        WheelScroll(element, direction, amount);
    }

    public void WaitFor(ElementTarget? target, string? text, bool untilGone, TimeSpan timeout)
    {
        if (target is not null)
        {
            if (target.Selector is { } selector)
            {
                var eventRoot = selector.ScopeRef is null
                    ? RequireTarget()
                    : Resolve(selector.ScopeRef);
                WaitWithEvents(
                    eventRoot,
                    TreeScope.Subtree,
                    SelectorProperties(eventRoot, selector),
                    () => SelectorExists(selector) != untilGone,
                    timeout,
                    untilGone
                        ? $"{Display(target)} was still present after {timeout.TotalSeconds:0}s."
                        : $"{Display(target)} did not appear within {timeout.TotalSeconds:0}s."
                );
                return;
            }

            var element = Resolve(target);
            var display = Display(target);
            WaitWithEvents(
                element,
                TreeScope.Element,
                [
                    element.Automation.PropertyLibrary.Element.IsEnabled,
                    element.Automation.PropertyLibrary.Element.IsOffscreen,
                ],
                () => IsActionable(element) != untilGone,
                timeout,
                untilGone
                    ? $"{display} was still present after {timeout.TotalSeconds:0}s."
                    : $"{display} did not become interactable within {timeout.TotalSeconds:0}s."
            );
            return;
        }

        var targetWindow = RequireTarget();
        WaitWithEvents(
            targetWindow,
            TreeScope.Subtree,
            [targetWindow.Automation.PropertyLibrary.Element.Name],
            () => ContainsText(targetWindow, text!) != untilGone,
            timeout,
            untilGone
                ? $"Text '{text}' was still present after {timeout.TotalSeconds:0}s."
                : $"Text '{text}' did not appear within {timeout.TotalSeconds:0}s."
        );
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
        _targetInfo = null;
        _refs.Clear();
    }

    public SessionStatus GetStatus() =>
        new()
        {
            DaemonProcessId = Environment.ProcessId,
            Target =
                _targetInfo is not null && ProcessInterop.IsWindow(_targetInfo.WindowHandle)
                    ? _targetInfo
                    : null,
            SnapshotGeneration = _generation,
            RefCount = _refs.Count,
        };

    public void Dispose()
    {
        _app?.Dispose();
        _automation.Dispose();
    }

    private static void SelectItemCore(AutomationElement element, string item, TimeSpan timeout)
    {
        var match = element.Patterns.ItemContainer.PatternOrDefault?.FindItemByProperty(
            null,
            element.Automation.PropertyLibrary.Element.Name,
            item
        );
        match ??= FindDescendantByName(element, item);
        if (match is null && element.Properties.ControlType.ValueOrDefault == ControlType.ComboBox)
        {
            element.Patterns.ExpandCollapse.PatternOrDefault?.Expand();
            Poller.WaitUntil(
                () => FindDescendantByName(element, item) is not null,
                timeout,
                $"No descendant named '{item}' appeared."
            );
            match = FindDescendantByName(element, item);
        }

        if (match is null)
        {
            throw new AutomationException(
                ErrorCodes.NotFound,
                $"No descendant named '{item}' found."
            );
        }

        match.Patterns.VirtualizedItem.PatternOrDefault?.Realize();

        var pattern =
            match.Patterns.SelectionItem.PatternOrDefault
            ?? throw new AutomationException(
                ErrorCodes.PatternUnsupported,
                $"'{item}' does not support selection."
            );
        pattern.Select();
    }

    private static AutomationElement? FindDescendantByName(
        AutomationElement element,
        string item
    ) => element.FindFirstDescendant(cf => cf.ByName(item, PropertyConditionFlags.IgnoreCase));

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

        var scrollAmount = ToScrollAmount(direction);
        var steps = Math.Max(1, (int)Math.Round(amount));
        ScrollPattern(pattern, vertical, scrollAmount, steps);
        return true;
    }

    private static ScrollAmount ToScrollAmount(ScrollDirection direction) =>
        direction is ScrollDirection.Up or ScrollDirection.Left
            ? ScrollAmount.SmallDecrement
            : ScrollAmount.SmallIncrement;

    private static void ScrollPattern(
        FlaUI.Core.Patterns.IScrollPattern pattern,
        bool vertical,
        ScrollAmount scrollAmount,
        int steps
    )
    {
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
    }

    private static void WheelScroll(
        AutomationElement element,
        ScrollDirection direction,
        double amount
    )
    {
        var rect = element.Properties.BoundingRectangle.ValueOrDefault;
        if (rect.IsEmpty)
        {
            return;
        }

        NativeInput.Scroll(RectConversions.Center(rect), direction, amount);
    }

    private static bool ContainsText(AutomationElement root, string text)
    {
        try
        {
            return root.FindFirstDescendant(cf => cf.ByName(text, (PropertyConditionFlags)3))
                is not null;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static PropertyId[] SelectorProperties(AutomationElement root, ElementSelector selector)
    {
        var properties = root.Automation.PropertyLibrary.Element;
        var ids = new List<PropertyId>();
        if (!string.IsNullOrEmpty(selector.AutomationId))
        {
            ids.Add(properties.AutomationId);
        }

        if (!string.IsNullOrEmpty(selector.Name) || !string.IsNullOrEmpty(selector.NameContains))
        {
            ids.Add(properties.Name);
        }

        if (!string.IsNullOrEmpty(selector.Role))
        {
            ids.Add(properties.ControlType);
        }

        return [.. ids];
    }

    private static void WaitWithEvents(
        AutomationElement root,
        TreeScope scope,
        PropertyId[] propertyIds,
        Func<bool> condition,
        TimeSpan timeout,
        string timeoutMessage
    )
    {
        if (condition())
        {
            return;
        }

        using var wake = new AutoResetEvent(initialState: false);
        var handlers = new List<EventHandlerBase>();
        try
        {
            handlers.AddRange(RegisterWaitHandlers(root, scope, propertyIds, wake));

            if (condition())
            {
                return;
            }

            var started = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(started) < timeout)
            {
                var remaining = timeout - Stopwatch.GetElapsedTime(started);
                wake.WaitOne(
                    remaining < TimeSpan.FromMilliseconds(250)
                        ? remaining
                        : TimeSpan.FromMilliseconds(250)
                );
                if (condition())
                {
                    return;
                }
            }
        }
        catch (COMException)
        {
            Poller.WaitUntil(condition, timeout, timeoutMessage);
        }
        finally
        {
            foreach (var handler in handlers)
            {
                Unregister(root, handler);
            }
        }

        if (condition())
        {
            return;
        }

        throw new AutomationException(ErrorCodes.Timeout, timeoutMessage);
    }

    private static List<EventHandlerBase> RegisterWaitHandlers(
        AutomationElement root,
        TreeScope scope,
        PropertyId[] propertyIds,
        EventWaitHandle wake
    )
    {
        var handlers = new List<EventHandlerBase>
        {
            root.RegisterStructureChangedEvent(scope, (_, _, _) => wake.Set()),
        };
        if (propertyIds.Length != 0)
        {
            handlers.Add(
                root.RegisterPropertyChangedEvent(scope, (_, _, _) => wake.Set(), propertyIds)
            );
        }

        return handlers;
    }

    private static void Unregister(AutomationElement root, EventHandlerBase handler)
    {
        try
        {
            if (handler is StructureChangedEventHandlerBase structure)
            {
                root.FrameworkAutomationElement.UnregisterStructureChangedEventHandler(structure);
            }
            else if (handler is PropertyChangedEventHandlerBase property)
            {
                root.FrameworkAutomationElement.UnregisterPropertyChangedEventHandler(property);
            }
        }
        catch (COMException)
        {
            // The provider vanished while the handler was being removed.
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
            : RectConversions.Center(rect);
    }

    private static AutomationException PatternUnsupported(string display, string pattern) =>
        new(ErrorCodes.PatternUnsupported, $"{display} does not support the {pattern} pattern.");

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

    private static ConditionBase BuildCondition(ConditionFactory factory, ElementSelector selector)
    {
        var conditions = new List<ConditionBase>();
        if (!string.IsNullOrEmpty(selector.AutomationId))
        {
            conditions.Add(
                factory.ByAutomationId(selector.AutomationId, PropertyConditionFlags.IgnoreCase)
            );
        }

        if (!string.IsNullOrEmpty(selector.Name))
        {
            conditions.Add(factory.ByName(selector.Name, PropertyConditionFlags.IgnoreCase));
        }

        if (!string.IsNullOrEmpty(selector.NameContains))
        {
            conditions.Add(factory.ByName(selector.NameContains, (PropertyConditionFlags)3));
        }

        if (!string.IsNullOrEmpty(selector.Role))
        {
            if (!RoleMapper.TryGetControlType(selector.Role, out var controlType))
            {
                throw new AutomationException(
                    ErrorCodes.BadRequest,
                    $"Unknown role '{selector.Role}'."
                );
            }

            conditions.Add(factory.ByControlType(controlType));
        }

        return conditions.Count == 1 ? conditions[0] : new AndCondition(conditions);
    }

    private static string Display(ElementTarget target) =>
        target.ElementRef is { } elementRef
            ? ElementRef.Display(elementRef)
            : $"selector({Describe(target.Selector!)})";

    private static string Describe(ElementSelector selector)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(selector.AutomationId))
        {
            parts.Add($"automationId='{selector.AutomationId}'");
        }

        if (!string.IsNullOrEmpty(selector.Name))
        {
            parts.Add($"name='{selector.Name}'");
        }

        if (!string.IsNullOrEmpty(selector.NameContains))
        {
            parts.Add($"nameContains='{selector.NameContains}'");
        }

        if (!string.IsNullOrEmpty(selector.Role))
        {
            parts.Add($"role='{selector.Role}'");
        }

        return string.Join(", ", parts);
    }

    private static AutomationException SelectorNotFound(ElementSelector selector) =>
        new(ErrorCodes.NotFound, $"No element matched selector({Describe(selector)}).");

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

    private AutomationElement[] FindElements(ElementSelector selector, bool all)
    {
        var root = selector.ScopeRef is null ? RequireTarget() : Resolve(selector.ScopeRef);
        var condition = BuildCondition(root.Automation.ConditionFactory, selector);
        if (!all)
        {
            var first = root.FindFirst(TreeScope.Subtree, condition);
            return first is null ? [] : [first];
        }

        return root.FindAll(TreeScope.Subtree, condition);
    }

    private UiNode CreateFindNode(AutomationElement element)
    {
        var elementRef = string.Create(CultureInfo.InvariantCulture, $"e{_nextRefIndex}");
        _nextRefIndex++;
        _refs[elementRef] = element;
        var controlType = element.Properties.ControlType.ValueOrDefault;
        return new UiNode
        {
            Role = RoleMapper.ToRole(controlType),
            Name = element.Properties.Name.ValueOrDefault,
            AutomationId = element.Properties.AutomationId.ValueOrDefault,
            Ref = elementRef,
            Bounds = RectConversions.ToBoundingRect(
                element.Properties.BoundingRectangle.ValueOrDefault
            ),
            States = [],
            Children = [],
        };
    }

    private bool SelectorExists(ElementSelector selector)
    {
        try
        {
            return FindElements(selector, all: false).Length != 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private void SetTarget(Window window, WindowInfo info)
    {
        _target = window;
        _targetInfo = info;
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
            : element;

    private AutomationElement Resolve(ElementTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ElementRef is { } elementRef)
        {
            return Resolve(elementRef);
        }

        var selector = target.Selector!;
        var matches = FindElements(selector, selector.RequireUnique);
        var error = matches.Length switch
        {
            0 => SelectorNotFound(selector),
            _ when selector.RequireUnique && matches.Length != 1 => new AutomationException(
                ErrorCodes.Ambiguous,
                $"Selector matched {matches.Length} elements; refine it."
            ),
            _ => null,
        };
        return error is null ? matches[0] : throw error;
    }

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

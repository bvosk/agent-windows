using System.Globalization;
using AgentWindows.Core.Elements;
using AgentWindows.Core.Input;
using AgentWindows.Core.Session;
using AgentWindows.Core.Snapshots;
using AgentWindows.Core.Windows;

namespace AgentWindows.Core.Tests.Fakes;

public sealed class FakeAutomationSession : IAutomationSession
{
    public IList<string> Calls { get; } = [];

    public Exception? ThrowOnNextCall { get; set; }

    public WindowInfo Window { get; set; } =
        new()
        {
            Title = "Test Window",
            WindowHandle = 0x1234,
            ProcessId = 42,
            ProcessName = "testapp",
        };

    public SnapshotResult Snapshot { get; set; } =
        new()
        {
            Root = new UiNode { Role = "window", Ref = "e1" },
            Generation = 1,
        };

    public void Dispose() => Record("dispose");

    public IReadOnlyList<WindowInfo> ListWindows()
    {
        Record("list");
        return [Window];
    }

    public WindowInfo Launch(string path, string? arguments, TimeSpan timeout)
    {
        Record($"launch path={path} args={arguments} timeout={Ms(timeout)}");
        return Window;
    }

    public WindowInfo Attach(string? title, int? processId, long? windowHandle)
    {
        Record($"attach title={title} pid={processId} hwnd={windowHandle}");
        return Window;
    }

    public SnapshotResult CaptureSnapshot(SnapshotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Record(
            $"snapshot interactive={options.InteractiveOnly} depth={options.MaxDepth} "
                + $"scope={options.ScopeRef}"
        );
        return Snapshot;
    }

    public void Click(
        ClickTarget target,
        MouseButtonKind button,
        bool doubleClick,
        TimeSpan timeout
    )
    {
        ArgumentNullException.ThrowIfNull(target);
        Record(
            $"click ref={target.ElementRef} x={target.X} y={target.Y} button={button} "
                + $"double={doubleClick} timeout={Ms(timeout)}"
        );
    }

    public void Fill(string elementRef, string text, TimeSpan timeout) =>
        Record($"fill ref={elementRef} text={text} timeout={Ms(timeout)}");

    public void Press(KeyChord chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        Record($"press modifiers={string.Join('+', chord.Modifiers)} key={chord.Key}");
    }

    public void SelectItem(string elementRef, string item, TimeSpan timeout) =>
        Record($"select ref={elementRef} item={item} timeout={Ms(timeout)}");

    public void Expand(string elementRef, bool collapse, TimeSpan timeout) =>
        Record($"expand ref={elementRef} collapse={collapse} timeout={Ms(timeout)}");

    public void Toggle(string elementRef, bool? desiredState, TimeSpan timeout) =>
        Record($"toggle ref={elementRef} state={desiredState} timeout={Ms(timeout)}");

    public void Scroll(
        string? elementRef,
        ScrollDirection direction,
        double amount,
        TimeSpan timeout
    ) =>
        Record(
            $"scroll ref={elementRef} direction={direction} amount={amount} timeout={Ms(timeout)}"
        );

    public void WaitFor(string? elementRef, string? text, bool untilGone, TimeSpan timeout) =>
        Record($"wait ref={elementRef} text={text} gone={untilGone} timeout={Ms(timeout)}");

    public string CaptureScreenshot(string? elementRef, string outputPath)
    {
        Record($"screenshot ref={elementRef} path={outputPath}");
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
        Record($"window action={kind} x={x} y={y} width={width} height={height}");
        return Window;
    }

    public void CloseTarget(bool force) => Record($"close force={force}");

    public SessionStatus GetStatus()
    {
        Record("status");
        return new SessionStatus
        {
            DaemonProcessId = 99,
            Target = Window,
            SnapshotGeneration = 1,
            RefCount = 3,
        };
    }

    private static string Ms(TimeSpan timeout) =>
        timeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture);

    private void Record(string call)
    {
        Calls.Add(call);
        if (ThrowOnNextCall is not { } exception)
        {
            return;
        }

        ThrowOnNextCall = null;
        throw exception;
    }
}

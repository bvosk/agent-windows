using AgentWindows.Core.Input;
using AgentWindows.Core.Snapshots;
using AgentWindows.Core.Windows;

namespace AgentWindows.Core.Session;

/// <summary>
/// A stateful automation session: holds the attached target window and the live
/// element table produced by the most recent snapshot. Refs are bare ids ("e5").
/// </summary>
public interface IAutomationSession : IDisposable
{
    public IReadOnlyList<WindowInfo> ListWindows();

    public WindowInfo Launch(string path, string? arguments, TimeSpan timeout);

    public WindowInfo Attach(string? title, int? processId, long? windowHandle);

    public SnapshotResult CaptureSnapshot(SnapshotOptions options);

    public void Click(
        ClickTarget target,
        MouseButtonKind button,
        bool doubleClick,
        TimeSpan timeout
    );

    public void Fill(string elementRef, string text, TimeSpan timeout);

    public void Press(KeyGesture gesture);

    public void SelectItem(string elementRef, string item, TimeSpan timeout);

    public void Expand(string elementRef, bool collapse, TimeSpan timeout);

    public void Toggle(string elementRef, bool? desiredState, TimeSpan timeout);

    public void Scroll(
        string? elementRef,
        ScrollDirection direction,
        double amount,
        TimeSpan timeout
    );

    public void WaitFor(string? elementRef, string? text, bool untilGone, TimeSpan timeout);

    public string CaptureScreenshot(string? elementRef, string outputPath);

    public WindowInfo PerformWindowAction(
        WindowActionKind kind,
        int? x,
        int? y,
        int? width,
        int? height
    );

    public void CloseTarget(bool force);

    public SessionStatus GetStatus();
}

using AgentWindows.Core.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace AgentWindows.Automation.Windows;

/// <summary>
/// Enumerates top-level windows via Win32 directly; orders of magnitude faster than
/// walking the UIA desktop element.
/// </summary>
public static partial class Win32WindowEnumerator
{
    private static readonly WNDENUMPROC _enumCallback = EnumCallback;

    [ThreadStatic]
    private static EnumerationState? _current;

    public static IReadOnlyList<WindowInfo> ListTopLevelWindows()
    {
        var state = new EnumerationState();
        _current = state;
        try
        {
            _ = PInvoke.EnumWindows(_enumCallback, default);
        }
        finally
        {
            _current = null;
        }

        return state.Windows;
    }

    private static BOOL EnumCallback(HWND hwnd, LPARAM lParam)
    {
        if (lParam != default)
        {
            return false;
        }

        var state = _current;
        if (state is null)
        {
            return false;
        }

        if (!PInvoke.IsWindowVisible(hwnd))
        {
            return true;
        }

        var title = GetWindowTitle(hwnd);
        if (title.Length == 0)
        {
            return true;
        }

        _ = PInvoke.GetWindowThreadProcessId(hwnd, out var processId);
        var pid = (int)processId;
        // Name and elevation are per-process facts; many processes own several windows.
        if (!state.ProcessInfo.TryGetValue(pid, out var info))
        {
            info = ProcessInterop.GetProcessInfo(pid);
            state.ProcessInfo[pid] = info;
        }

        state.Windows.Add(
            new WindowInfo
            {
                Title = title,
                WindowHandle = ((IntPtr)hwnd).ToInt64(),
                ProcessId = pid,
                ProcessName = info.Name,
                IsElevated = info.IsElevated,
                Bounds = GetBounds(hwnd),
            }
        );
        return true;
    }

    private static string GetWindowTitle(HWND hwnd)
    {
        var length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return "";
        }

        var buffer = new char[length + 1];
        var copied = PInvoke.GetWindowText(hwnd, buffer);
        return copied <= 0 ? "" : new string(buffer, 0, copied);
    }

    private static BoundingRect? GetBounds(HWND hwnd) =>
        PInvoke.GetWindowRect(hwnd, out var rect)
            ? new BoundingRect(rect.X, rect.Y, rect.Width, rect.Height)
            : null;

    private sealed class EnumerationState
    {
        public List<WindowInfo> Windows { get; } = [];

        public Dictionary<int, (string Name, bool? IsElevated)> ProcessInfo { get; } = [];
    }
}

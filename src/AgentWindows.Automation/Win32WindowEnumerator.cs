using System.Runtime.InteropServices;
using AgentWindows.Core.Model;

namespace AgentWindows.Automation;

/// <summary>
/// Enumerates top-level windows via Win32 directly; orders of magnitude faster than
/// walking the UIA desktop element.
/// </summary>
public static partial class Win32WindowEnumerator
{
    [ThreadStatic]
    private static EnumerationState? _current;

    public static IReadOnlyList<WindowInfo> ListTopLevelWindows()
    {
        var state = new EnumerationState();
        _current = state;
        try
        {
            unsafe
            {
                _ = NativeMethods.EnumWindows(&EnumCallback, IntPtr.Zero);
            }
        }
        finally
        {
            _current = null;
        }

        return state.Windows;
    }

    [UnmanagedCallersOnly]
    private static int EnumCallback(IntPtr hwnd, IntPtr lParam)
    {
        var state = _current;
        if (state is null)
        {
            return 0;
        }

        if (!NativeMethods.IsWindowVisible(hwnd))
        {
            return 1;
        }

        var title = GetWindowTitle(hwnd);
        if (title.Length == 0)
        {
            return 1;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
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
                WindowHandle = hwnd.ToInt64(),
                ProcessId = pid,
                ProcessName = info.Name,
                IsElevated = info.IsElevated,
                Bounds = GetBounds(hwnd),
            }
        );
        return 1;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return "";
        }

        var buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowTextW(hwnd, buffer, buffer.Length);
        return copied <= 0 ? "" : new string(buffer, 0, copied);
    }

    private static BoundingRect? GetBounds(IntPtr hwnd) =>
        NativeMethods.GetWindowRect(hwnd, out var rect)
            ? new BoundingRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static partial class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool EnumWindows(
            delegate* unmanaged<IntPtr, IntPtr, int> lpEnumFunc,
            IntPtr lParam
        );

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsWindowVisible(IntPtr hwnd);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", SetLastError = true)]
        internal static partial int GetWindowTextLengthW(IntPtr hwnd);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport(
            "user32.dll",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16
        )]
        internal static partial int GetWindowTextW(
            IntPtr hwnd,
            [Out] char[] lpString,
            int nMaxCount
        );

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll")]
        internal static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(IntPtr hwnd, out WindowRect lpRect);
    }

    private sealed class EnumerationState
    {
        public List<WindowInfo> Windows { get; } = [];

        public Dictionary<int, (string Name, bool? IsElevated)> ProcessInfo { get; } = [];
    }
}

using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;

namespace AgentWindows.Automation.Windows;

/// <summary>
/// Cheap per-process facts (name, elevation) read via Win32 from a single process
/// handle; avoids the cost of System.Diagnostics.Process on hot paths.
/// </summary>
public static partial class ProcessInterop
{
    public static bool CurrentProcessIsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsWindow(long windowHandle) =>
        PInvoke.IsWindow(new HWND((nint)windowHandle));

    /// <summary>
    /// Reads the process name and token elevation from one process handle. Fields
    /// are ""/null when the process is gone or not accessible.
    /// </summary>
    public static (string Name, bool? IsElevated) GetProcessInfo(int processId)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
            bInheritHandle: false,
            (uint)processId
        );
        if (process.IsInvalid)
        {
            return ("", null);
        }

        return (ReadProcessName(process), ReadTokenElevation(process));
    }

    private static string ReadProcessName(SafeFileHandle process)
    {
        var buffer = new char[1024];
        var capacity = (uint)buffer.Length;
        if (
            !PInvoke.QueryFullProcessImageName(
                process,
                PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32,
                buffer,
                ref capacity
            )
            || capacity == 0
        )
        {
            return "";
        }

        var path = new string(buffer, 0, checked((int)capacity));
        return Path.GetFileNameWithoutExtension(path);
    }

    private static bool? ReadTokenElevation(SafeFileHandle process)
    {
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
        {
            token.Dispose();
            return null;
        }

        using (token)
        {
            Span<byte> elevation = stackalloc byte[sizeof(int)];
            return PInvoke.GetTokenInformation(
                token,
                TOKEN_INFORMATION_CLASS.TokenElevation,
                elevation,
                out _
            )
                ? MemoryMarshal.Read<int>(elevation) != 0
                : null;
        }
    }
}

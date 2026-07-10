using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AgentWindows.Automation.Windows;

/// <summary>
/// Cheap per-process facts (name, elevation) read via Win32 from a single process
/// handle; avoids the cost of System.Diagnostics.Process on hot paths.
/// </summary>
public static partial class ProcessInterop
{
    private const int _processQueryLimitedInformation = 0x1000;
    private const int _tokenQuery = 0x0008;
    private const int _tokenElevation = 20;

    public static bool CurrentProcessIsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool IsWindow(long windowHandle) =>
        NativeMethods.IsNativeWindow(new IntPtr(windowHandle));

    /// <summary>
    /// Reads the process name and token elevation from one process handle. Fields
    /// are ""/null when the process is gone or not accessible.
    /// </summary>
    public static (string Name, bool? IsElevated) GetProcessInfo(int processId)
    {
        var process = NativeMethods.OpenProcess(
            _processQueryLimitedInformation,
            bInheritHandle: false,
            processId
        );
        if (process == IntPtr.Zero)
        {
            return ("", null);
        }

        try
        {
            return (ReadProcessName(process), ReadTokenElevation(process));
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static string ReadProcessName(IntPtr process)
    {
        var capacity = 1024;
        var buffer = new char[capacity];
        if (
            !NativeMethods.QueryFullProcessImageNameW(process, 0, buffer, ref capacity)
            || capacity <= 0
        )
        {
            return "";
        }

        var path = new string(buffer, 0, capacity);
        return Path.GetFileNameWithoutExtension(path);
    }

    private static bool? ReadTokenElevation(IntPtr process)
    {
        if (!NativeMethods.OpenProcessToken(process, _tokenQuery, out var token))
        {
            return null;
        }

        try
        {
            var elevation = 0;
            return NativeMethods.GetTokenInformation(
                token,
                _tokenElevation,
                ref elevation,
                sizeof(int),
                out _
            )
                ? elevation != 0
                : null;
        }
        finally
        {
            NativeMethods.CloseHandle(token);
        }
    }

    private static partial class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("user32.dll", EntryPoint = "IsWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool IsNativeWindow(IntPtr windowHandle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr handle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial IntPtr OpenProcess(
            int dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            int dwProcessId
        );

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport(
            "kernel32.dll",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16
        )]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool QueryFullProcessImageNameW(
            IntPtr hProcess,
            int dwFlags,
            [Out] char[] lpExeName,
            ref int lpdwSize
        );

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool OpenProcessToken(
            IntPtr processHandle,
            int desiredAccess,
            out IntPtr tokenHandle
        );

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetTokenInformation(
            IntPtr tokenHandle,
            int tokenInformationClass,
            ref int tokenInformation,
            int tokenInformationLength,
            out int returnLength
        );
    }
}

using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AgentWindows.Automation;

public static partial class ElevationDetector
{
    private const int _processQueryLimitedInformation = 0x1000;
    private const int _tokenQuery = 0x0008;
    private const int _tokenElevation = 20;

    public static bool CurrentProcessIsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Reads the target process token's elevation directly. Returns null when the
    /// process is gone or its token is not accessible.
    /// </summary>
    public static bool? ProcessIsElevated(int processId)
    {
        var process = NativeMethods.OpenProcess(
            _processQueryLimitedInformation,
            bInheritHandle: false,
            processId
        );
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(process, _tokenQuery, out var token))
            {
                return null;
            }

            try
            {
                return ReadTokenElevation(token);
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>Cheap process-name lookup (no Process object, no snapshot).</summary>
    public static string GetProcessName(int processId)
    {
        var process = NativeMethods.OpenProcess(
            _processQueryLimitedInformation,
            bInheritHandle: false,
            processId
        );
        if (process == IntPtr.Zero)
        {
            return "";
        }

        try
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
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static bool? ReadTokenElevation(IntPtr token)
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

    private static partial class NativeMethods
    {
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

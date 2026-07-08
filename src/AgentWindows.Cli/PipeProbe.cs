using System.Runtime.InteropServices;

namespace AgentWindows.Cli;

/// <summary>
/// Instant named-pipe existence check. File.Exists on \\.\pipe\ paths is
/// unreliable; WaitNamedPipe(0) is the canonical probe and distinguishes
/// "no such pipe" from "exists but busy".
/// </summary>
public static partial class PipeProbe
{
    private const int _errorFileNotFound = 2;

    public static bool Exists(string pipeName)
    {
        if (NativeMethods.WaitNamedPipeW($@"\\.\pipe\{pipeName}", 0))
        {
            return true;
        }

        // Busy or timed-out pipes exist; only file-not-found means no daemon.
        return Marshal.GetLastPInvokeError() != _errorFileNotFound;
    }

    private static partial class NativeMethods
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport(
            "kernel32.dll",
            SetLastError = true,
            StringMarshalling = StringMarshalling.Utf16
        )]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool WaitNamedPipeW(string name, int timeout);
    }
}

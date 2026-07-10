using System.Runtime.InteropServices;
using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Cli.Daemon;

/// <summary>
/// Instant named-pipe existence check. File.Exists on \\.\pipe\ paths is
/// unreliable; WaitNamedPipe(0) is the canonical probe and distinguishes
/// "no such pipe" from "exists but busy".
/// </summary>
public static partial class PipeProbe
{
    private const int _errorFileNotFound = 2;
    private const string _pipeDirectory = @"\\.\pipe\";

    public static IReadOnlyList<string> ListSessions() => ListSessions(Directory.EnumerateFiles);

    public static bool Exists(string pipeName) =>
        Exists(pipeName, NativeMethods.WaitNamedPipeW, Marshal.GetLastPInvokeError);

    internal static IReadOnlyList<string> ListSessions(
        Func<string, string, IEnumerable<string>> enumerateFiles
    ) =>
        enumerateFiles(_pipeDirectory, $"{PipeNames.Prefix}*")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => name.StartsWith(PipeNames.Prefix, StringComparison.Ordinal))
            .Select(name => name[PipeNames.Prefix.Length..])
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    internal static bool Exists(
        string pipeName,
        Func<string, int, bool> waitNamedPipe,
        Func<int> getLastError
    )
    {
        if (waitNamedPipe($@"\\.\pipe\{pipeName}", 0))
        {
            return true;
        }

        // Busy or timed-out pipes exist; only file-not-found means no daemon.
        return getLastError() != _errorFileNotFound;
    }

    private static partial class NativeMethods
    {
        // CsWin32 does not currently preserve WaitNamedPipe's last error. Keep this
        // binding explicit because Exists relies on ERROR_FILE_NOT_FOUND.
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

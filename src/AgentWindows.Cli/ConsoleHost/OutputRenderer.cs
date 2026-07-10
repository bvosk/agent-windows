using System.Globalization;
using System.Text;
using AgentWindows.Core.Protocol.Capture;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Protocol.Windows;
using AgentWindows.Core.Session;
using AgentWindows.Core.Snapshots;
using AgentWindows.Core.Windows;

namespace AgentWindows.Cli.ConsoleHost;

/// <summary>Renders a daemon response as human-readable text or the raw JSON envelope.</summary>
public static class OutputRenderer
{
    public static int Render(
        DaemonResponse response,
        bool json,
        TextWriter output,
        TextWriter error
    )
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (json)
        {
            output.WriteLine(ProtocolSerializer.SerializeResponse(response));
            return response.Ok ? 0 : 1;
        }

        if (!response.Ok)
        {
            error.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"error [{response.ErrorCode}]: {response.Message}"
                )
            );
            return 1;
        }

        output.Write(RenderPayload(response.Payload));
        return 0;
    }

    public static string RenderPayload(ResponsePayload? payload) =>
        payload switch
        {
            WindowListPayload p => RenderWindowList(p.Windows),
            WindowPayload p => RenderWindow(p.Window) + "\n",
            SnapshotPayload p => SnapshotTextFormatter.Format(p.Root),
            ScreenshotPayload p => $"saved: {p.Path}\n",
            StatusPayload p => RenderStatus(p.Status),
            AckPayload p => $"ok: {p.Detail ?? "done"}\n",
            _ => "ok\n",
        };

    private static string RenderWindowList(IReadOnlyList<WindowInfo> windows)
    {
        if (windows.Count == 0)
        {
            return "no top-level windows found\n";
        }

        var builder = new StringBuilder();
        foreach (var window in windows)
        {
            builder.Append(RenderWindow(window)).Append('\n');
        }

        return builder.ToString();
    }

    private static string RenderWindow(WindowInfo window)
    {
        var elevated = window.IsElevated == true ? " elevated" : "";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"\"{window.Title}\" pid={window.ProcessId} process={window.ProcessName} "
                + $"hwnd=0x{window.WindowHandle:X}{elevated}"
        );
    }

    private static string RenderStatus(SessionStatus status)
    {
        var target = status.Target is null ? "none" : RenderWindow(status.Target);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"daemon: pid {status.DaemonProcessId}\ntarget: {target}\n"
                + $"snapshot generation: {status.SnapshotGeneration} ({status.RefCount} refs)\n"
        );
    }
}

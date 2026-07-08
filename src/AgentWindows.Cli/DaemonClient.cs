using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli;

/// <summary>
/// Thin named-pipe client. Auto-spawns the daemon (this same executable with
/// 'daemon run') when it is not yet running.
/// </summary>
public sealed class DaemonClient(string session)
{
    private static readonly TimeSpan _spawnTimeout = TimeSpan.FromSeconds(10);
    private readonly string _pipeName = PipeNames.For(session);
    private readonly string _session = session;

    public DaemonResponse Send(DaemonRequest request, bool spawnIfMissing = true)
    {
        using var pipe = Connect(spawnIfMissing);
        if (pipe is null)
        {
            return DaemonResponse.Success(new AckPayload { Detail = "daemon not running" });
        }

        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        writer.WriteLine(ProtocolSerializer.SerializeRequest(request));
        var line =
            reader.ReadLine()
            ?? throw new AutomationException(
                ErrorCodes.InternalError,
                "The daemon closed the connection without responding."
            );
        return ProtocolSerializer.DeserializeResponse(line)
            ?? throw new AutomationException(
                ErrorCodes.InternalError,
                "The daemon sent an unparseable response."
            );
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the connected pipe transfers to the caller, which disposes it."
    )]
    private NamedPipeClientStream? Connect(bool spawnIfMissing)
    {
        if (TryConnect(TimeSpan.FromMilliseconds(250)) is { } pipe)
        {
            return pipe;
        }

        if (!spawnIfMissing)
        {
            return null;
        }

        SpawnDaemon();
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < _spawnTimeout)
        {
            if (TryConnect(TimeSpan.FromMilliseconds(500)) is { } spawned)
            {
                return spawned;
            }
        }

        throw new AutomationException(
            ErrorCodes.InternalError,
            "Could not reach the daemon after starting it. "
                + "Try 'agent-windows daemon stop' and run the command again."
        );
    }

    private NamedPipeClientStream? TryConnect(TimeSpan timeout)
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
        try
        {
            pipe.Connect((int)timeout.TotalMilliseconds);
            return pipe;
        }
        catch (TimeoutException)
        {
            pipe.Dispose();
            return null;
        }
        catch (IOException)
        {
            // Pipe exists but is busy with another client; treat as not reachable yet.
            pipe.Dispose();
            return null;
        }
    }

    private void SpawnDaemon()
    {
        var executable =
            Environment.ProcessPath
            ?? throw new AutomationException(
                ErrorCodes.InternalError,
                "Cannot determine the agent-windows executable path to start the daemon."
            );
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("daemon");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add(_session);
        using var process = Process.Start(startInfo);
    }
}

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli;

/// <summary>
/// Named-pipe client that keeps one connection open across sends (the REPL sends
/// many requests per process). Auto-spawns the daemon when it is not yet running.
/// </summary>
public sealed class DaemonClient(string session) : IDisposable
{
    private static readonly TimeSpan _spawnTimeout = TimeSpan.FromSeconds(10);
    private readonly string _pipeName = PipeNames.For(session);
    private readonly string _session = session;
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public DaemonResponse Send(DaemonRequest request, bool spawnIfMissing = true)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!EnsureConnected(spawnIfMissing))
            {
                return DaemonResponse.Success(new AckPayload { Detail = "daemon not running" });
            }

            try
            {
                _writer!.WriteLine(ProtocolSerializer.SerializeRequest(request));
                var line = _reader!.ReadLine();
                if (line is null)
                {
                    // The daemon went away mid-conversation; reconnect and retry once.
                    ResetConnection();
                    continue;
                }

                return ProtocolSerializer.DeserializeResponse(line)
                    ?? throw new AutomationException(
                        ErrorCodes.InternalError,
                        "The daemon sent an unparseable response."
                    );
            }
            catch (IOException)
            {
                ResetConnection();
            }
        }

        throw new AutomationException(
            ErrorCodes.InternalError,
            "Lost the connection to the daemon. Run the command again."
        );
    }

    public void Dispose() => ResetConnection();

    private void ResetConnection()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _pipe?.Dispose();
        _writer = null;
        _reader = null;
        _pipe = null;
    }

    private bool EnsureConnected(bool spawnIfMissing)
    {
        if (_pipe is { IsConnected: true })
        {
            return true;
        }

        ResetConnection();
        var pipe = Connect(spawnIfMissing);
        if (pipe is null)
        {
            return false;
        }

        _pipe = pipe;
        _reader = new StreamReader(pipe, leaveOpen: true);
        _writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        return true;
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
            // Give the daemon its own stdio pipes. Without this it inherits the
            // CLI's console handles, and callers that redirect the CLI's output
            // never see EOF because the long-lived daemon keeps the pipe open.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("daemon");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add(_session);
        using var process = Process.Start(startInfo);
    }
}

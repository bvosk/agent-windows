using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli.Daemon;

/// <summary>
/// Named-pipe client that keeps one connection open across sends (the REPL sends
/// many requests per process). Auto-spawns the daemon when it is not yet running.
/// </summary>
public sealed class DaemonClient : IDisposable
{
    private static readonly TimeSpan _spawnTimeout = TimeSpan.FromSeconds(10);
    private readonly Func<string?> _getProcessPath;
    private readonly Func<long, TimeSpan> _getElapsedTime;
    private readonly Func<long> _getTimestamp;
    private readonly string _pipeName;
    private readonly Func<string, bool> _pipeExists;
    private readonly Func<string, int, IDaemonConnection> _connect;
    private readonly string _session;
    private readonly Func<ProcessStartInfo, IDisposable?> _startProcess;
    private IDaemonConnection? _connection;

    public DaemonClient(string session)
        : this(
            session,
            PipeProbe.Exists,
            NamedPipeConnection.Connect,
            static () => Environment.ProcessPath,
            static startInfo => Process.Start(startInfo),
            Stopwatch.GetTimestamp,
            Stopwatch.GetElapsedTime
        ) { }

    internal DaemonClient(
        string session,
        Func<string, bool> pipeExists,
        Func<string, int, IDaemonConnection> connect,
        Func<string?> getProcessPath,
        Func<ProcessStartInfo, IDisposable?> startProcess,
        Func<long> getTimestamp,
        Func<long, TimeSpan> getElapsedTime
    )
    {
        _session = session;
        _pipeName = PipeNames.For(session);
        _pipeExists = pipeExists;
        _connect = connect;
        _getProcessPath = getProcessPath;
        _startProcess = startProcess;
        _getTimestamp = getTimestamp;
        _getElapsedTime = getElapsedTime;
    }

    public DaemonResponse Send(DaemonRequest request, bool spawnIfMissing = true)
    {
        var line = SendRaw(request, spawnIfMissing);
        return ProtocolSerializer.DeserializeResponse(line)
            ?? throw new AutomationException(
                ErrorCodes.InternalError,
                "The daemon sent an unparseable response."
            );
    }

    public string SendRaw(DaemonRequest request, bool spawnIfMissing = true)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!EnsureConnected(spawnIfMissing))
            {
                return ProtocolSerializer.SerializeResponse(
                    DaemonResponse.Success(new AckPayload { Detail = "daemon not running" })
                );
            }

            try
            {
                _connection!.WriteLine(ProtocolSerializer.SerializeRequest(request));
                var line = _connection.ReadLine();
                if (line is null)
                {
                    // The daemon went away mid-conversation; reconnect and retry once.
                    ResetConnection();
                    continue;
                }

                return line;
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
        _connection?.Dispose();
        _connection = null;
    }

    private bool EnsureConnected(bool spawnIfMissing)
    {
        if (_connection is not null)
        {
            return true;
        }

        var connection = Connect(spawnIfMissing);
        if (connection is null)
        {
            return false;
        }

        _connection = connection;
        return true;
    }

    private IDaemonConnection? Connect(bool spawnIfMissing)
    {
        // Probing pipe existence is free; NamedPipeClientStream.Connect(timeout)
        // otherwise burns its full timeout retrying when no daemon exists.
        var pipeExists = _pipeExists(_pipeName);
        if (pipeExists && TryConnect(250) is { } pipe)
        {
            return pipe;
        }

        if (!spawnIfMissing)
        {
            return null;
        }

        if (!pipeExists)
        {
            // Absent pipe means no daemon: spawn immediately. An existing-but-busy
            // pipe (e.g. a REPL holds it) must NOT spawn a doomed duplicate; just
            // keep retrying until the holder releases it or the deadline passes.
            SpawnDaemon();
        }

        var started = _getTimestamp();
        while (_getElapsedTime(started) < _spawnTimeout)
        {
            if (TryConnect(500) is { } spawned)
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

    private IDaemonConnection? TryConnect(int timeoutMs)
    {
        try
        {
            return _connect(_pipeName, timeoutMs);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (IOException)
        {
            // Pipe exists but is busy with another client; treat as not reachable yet.
            return null;
        }
    }

    private void SpawnDaemon()
    {
        var clientExecutable =
            _getProcessPath()
            ?? throw new AutomationException(
                ErrorCodes.InternalError,
                "Cannot determine the agent-windows executable path to start the daemon."
            );
        var siblingDaemon = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(clientExecutable))!,
            "agent-windows-daemon.exe"
        );
        var executable = File.Exists(siblingDaemon) ? siblingDaemon : clientExecutable;
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            // Process.Start enables bInheritHandles when any standard stream is
            // redirected. The daemon can then inherit unrelated redirected handles
            // from this CLI process and keep its caller from observing EOF. Shell
            // execution avoids that inheritance path; hide the daemon's console.
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("daemon");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add(_session);
        using var process = _startProcess(startInfo);
    }
}

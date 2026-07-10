using System.Diagnostics;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli.Daemon;

public static class DaemonManager
{
    private static readonly Func<long, TimeSpan> _getElapsedTime = Stopwatch.GetElapsedTime;
    private static readonly Func<long> _getTimestamp = Stopwatch.GetTimestamp;
    private static readonly Func<IReadOnlyList<string>> _listSessions = PipeProbe.ListSessions;
    private static readonly Func<string, bool> _pipeExists = PipeProbe.Exists;
    private static readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);
    private static readonly Action<TimeSpan> _sleep = Thread.Sleep;
    private static readonly Func<string, DaemonResponse> _stopSession = StopSession;

    public static DaemonResponse StopAll() =>
        StopAll(_listSessions, _pipeExists, _stopSession, _getTimestamp, _getElapsedTime, _sleep);

    internal static DaemonResponse StopAll(
        Func<IReadOnlyList<string>> listSessions,
        Func<string, bool> pipeExists,
        Func<string, DaemonResponse> stopSession,
        Func<long> getTimestamp,
        Func<long, TimeSpan> getElapsedTime,
        Action<TimeSpan> sleep
    )
    {
        var sessions = listSessions();
        foreach (var session in sessions)
        {
            var response = stopSession(session);
            if (!response.Ok)
            {
                return response;
            }
        }

        var started = getTimestamp();
        while (sessions.Any(session => pipeExists(PipeNames.For(session))))
        {
            if (getElapsedTime(started) >= _shutdownTimeout)
            {
                throw new AutomationException(
                    ErrorCodes.InternalError,
                    "Timed out waiting for all daemons to stop. A session may be busy with "
                        + "another client."
                );
            }

            sleep(TimeSpan.FromMilliseconds(25));
        }

        var detail =
            sessions.Count == 0
                ? "no daemons running"
                : $"stopped {sessions.Count} daemon(s): {string.Join(", ", sessions)}";
        return DaemonResponse.Success(new AckPayload { Detail = detail });
    }

    private static DaemonResponse StopSession(string session)
    {
        using var client = new DaemonClient(session);
        return client.Send(new ShutdownRequest(), spawnIfMissing: false);
    }
}

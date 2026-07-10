using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using AgentWindows.Automation.Session;
using AgentWindows.Core.Dispatch;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli.Daemon;

/// <summary>
/// The daemon: owns the single automation session and serves CLI clients one at a
/// time over a named pipe until a shutdown request arrives.
/// </summary>
public static class DaemonHost
{
    [ExcludeFromCodeCoverage(
        Justification = "Windows/FlaUI and native named-pipe composition root; validated by "
            + "daemon-lifecycle end-to-end tests."
    )]
    public static int Run(string session)
    {
        var pipeName = PipeNames.For(session);
        using var automationSession = new FlaUiSession();
        var dispatcher = new RequestDispatcher(automationSession);
        return RunLoop(
            pipeName,
            static name => CreateServer(name, CreateNamedPipeServer),
            static server => ((NamedPipeServerStream)server).WaitForConnection(),
            dispatcher.Dispatch,
            Stopwatch.GetTimestamp,
            Stopwatch.GetElapsedTime
        );
    }

    internal static int RunLoop(
        string pipeName,
        Func<string, Stream?> createServer,
        Action<Stream> waitForConnection,
        Func<DaemonRequest, DaemonResponse> dispatch,
        Func<long> getTimestamp,
        Func<long, TimeSpan> getElapsedTime
    )
    {
        while (true)
        {
            using var server = createServer(pipeName);
            if (server is null)
            {
                // Another daemon already owns this session's pipe.
                return 0;
            }

            waitForConnection(server);
            if (!ServeClient(server, dispatch, getTimestamp, getElapsedTime))
            {
                return 0;
            }
        }
    }

    internal static Stream? CreateServer(string pipeName, Func<string, Stream> createServer)
    {
        try
        {
            return createServer(pipeName);
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Serves one client connection. Returns false when shutdown was requested.</summary>
    internal static bool ServeClient(
        Stream server,
        Func<DaemonRequest, DaemonResponse> dispatch,
        Func<long> getTimestamp,
        Func<long, TimeSpan> getElapsedTime
    )
    {
        try
        {
            using var reader = new StreamReader(server, leaveOpen: true);
            using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            while (reader.ReadLine() is { } line)
            {
                var started = getTimestamp();
                var request = ProtocolSerializer.DeserializeRequest(line);
                var response = request is null
                    ? DaemonResponse.Failure(
                        ErrorCodes.BadRequest,
                        "The daemon received an unparseable request."
                    )
                    : dispatch(request);
                response = response with
                {
                    ElapsedMs = Math.Round(getElapsedTime(started).TotalMilliseconds, 1),
                };
                writer.WriteLine(ProtocolSerializer.SerializeResponse(response));
                if (request is ShutdownRequest)
                {
                    return false;
                }
            }
        }
        catch (IOException)
        {
            // Client vanished mid-conversation; wait for the next one.
        }

        return true;
    }

    [ExcludeFromCodeCoverage(
        Justification = "Native named-pipe factory is exercised by daemon-lifecycle end-to-end "
            + "tests; creation policy is unit tested through CreateServer."
    )]
    private static Stream CreateNamedPipeServer(string pipeName) =>
        new NamedPipeServerStream(pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1);
}

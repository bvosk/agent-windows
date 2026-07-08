using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using AgentWindows.Automation;
using AgentWindows.Core.Dispatch;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli;

/// <summary>
/// The daemon: owns the single automation session and serves CLI clients one at a
/// time over a named pipe until a shutdown request arrives.
/// </summary>
public static class DaemonHost
{
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The server stream from CreateServer is disposed by the using declaration."
    )]
    public static int Run(string session)
    {
        var pipeName = PipeNames.For(session);
        using var automationSession = new FlaUiSession();
        var dispatcher = new RequestDispatcher(automationSession);
        while (true)
        {
            using var server = CreateServer(pipeName);
            if (server is null)
            {
                // Another daemon already owns this session's pipe.
                return 0;
            }

            server.WaitForConnection();
            if (!ServeClient(server, dispatcher))
            {
                return 0;
            }
        }
    }

    private static NamedPipeServerStream? CreateServer(string pipeName)
    {
        try
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1
            );
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Serves one client connection. Returns false when shutdown was requested.</summary>
    private static bool ServeClient(NamedPipeServerStream server, RequestDispatcher dispatcher)
    {
        try
        {
            using var reader = new StreamReader(server, leaveOpen: true);
            using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            while (reader.ReadLine() is { } line)
            {
                var request = ProtocolSerializer.DeserializeRequest(line);
                var response = request is null
                    ? DaemonResponse.Failure(
                        ErrorCodes.BadRequest,
                        "The daemon received an unparseable request."
                    )
                    : dispatcher.Dispatch(request);
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
}

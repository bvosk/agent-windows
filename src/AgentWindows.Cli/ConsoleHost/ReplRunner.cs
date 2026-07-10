using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli.ConsoleHost;

/// <summary>
/// Reads one command per stdin line (CLI syntax, or a raw JSON request when the
/// line starts with '{'), sends it to the daemon over a persistent connection, and
/// writes exactly one JSON envelope per line. Fails fast: the first unsuccessful
/// response ends the session with exit code 1.
/// </summary>
public sealed class ReplRunner(
    RootCommand root,
    CommandContext context,
    string session,
    Func<DaemonRequest, DaemonResponse> send,
    Func<DaemonRequest, string>? sendRaw = null
)
{
    private static readonly ParserConfiguration _parserConfiguration =
        CommandTree.CreateConfiguration();

    private readonly RootCommand _root = root;
    private readonly CommandContext _context = context;
    private readonly string _session = session;
    private readonly Func<DaemonRequest, DaemonResponse> _send = send;
    private readonly Func<DaemonRequest, string>? _sendRaw = sendRaw;

    public int Run(TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        while (input.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line is "exit" or "quit")
            {
                return 0;
            }

            var responseLine = ExecuteLineRaw(line);
            output.WriteLine(responseLine);
            output.Flush();
            if (!IsSuccess(responseLine))
            {
                return 1;
            }
        }

        return 0;
    }

    private static DaemonRequest ParseJsonLine(string line) =>
        ProtocolSerializer.DeserializeRequest(line)
        ?? throw new AutomationException(
            ErrorCodes.BadRequest,
            "Unparseable JSON request. Expected e.g. {\"cmd\":\"click\",\"ref\":\"e5\"}."
        );

    private static bool IsSuccess(string responseLine)
    {
        using var document = JsonDocument.Parse(responseLine);
        return document.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean();
    }

    private DaemonResponse ExecuteLine(string line)
    {
        try
        {
            var request = line.StartsWith('{') ? ParseJsonLine(line) : ParseCliLine(line);
            return _send(request);
        }
        catch (AutomationException ex)
        {
            return DaemonResponse.Failure(ex.Code, ex.Message);
        }
    }

    private string ExecuteLineRaw(string line)
    {
        if (_sendRaw is null)
        {
            return ProtocolSerializer.SerializeResponse(ExecuteLine(line));
        }

        try
        {
            var request = line.StartsWith('{') ? ParseJsonLine(line) : ParseCliLine(line);
            return _sendRaw(request);
        }
        catch (AutomationException ex)
        {
            return ProtocolSerializer.SerializeResponse(
                DaemonResponse.Failure(ex.Code, ex.Message)
            );
        }
    }

    private DaemonRequest ParseCliLine(string line)
    {
        var tokens = CommandLineParser.SplitCommandLine(line).ToArray();
        var parseResult = _root.Parse(tokens, _parserConfiguration);
        if (parseResult.Errors.Count > 0)
        {
            throw new AutomationException(
                ErrorCodes.BadRequest,
                string.Join(" ", parseResult.Errors.Select(e => e.Message))
            );
        }

        RejectForeignSession(parseResult);
        return _context.BuildRequest(parseResult)
            ?? throw new AutomationException(
                ErrorCodes.BadRequest,
                $"'{parseResult.CommandResult.Command.Name}' is not available inside the REPL."
            );
    }

    private void RejectForeignSession(ParseResult parseResult)
    {
        var explicitSession = _context.GetExplicitSession(parseResult);
        if (
            explicitSession is null
            || string.Equals(explicitSession, _session, StringComparison.Ordinal)
        )
        {
            return;
        }

        throw new AutomationException(
            ErrorCodes.BadRequest,
            $"--session cannot change inside a REPL (this session is '{_session}'). "
                + "Start another 'agent-windows repl --session <name>' instead."
        );
    }
}

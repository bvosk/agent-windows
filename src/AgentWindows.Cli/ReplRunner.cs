using System.CommandLine;
using System.CommandLine.Parsing;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli;

/// <summary>
/// Reads one command per stdin line (CLI syntax, or a raw JSON request when the
/// line starts with '{'), sends it to the daemon over a persistent connection, and
/// writes exactly one JSON envelope per line. Fails fast: the first unsuccessful
/// response ends the session with exit code 1.
/// </summary>
public sealed class ReplRunner(
    RootCommand root,
    CommandContext context,
    Func<DaemonRequest, DaemonResponse> send
)
{
    private readonly RootCommand _root = root;
    private readonly CommandContext _context = context;
    private readonly Func<DaemonRequest, DaemonResponse> _send = send;

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

            var response = ExecuteLine(line);
            output.WriteLine(ProtocolSerializer.SerializeResponse(response));
            output.Flush();
            if (!response.Ok)
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

    private DaemonRequest ParseCliLine(string line)
    {
        var tokens = CommandLineParser.SplitCommandLine(line).ToArray();
        var parseResult = _root.Parse(tokens, CommandTree.CreateConfiguration());
        return parseResult.Errors.Count > 0
            ? throw new AutomationException(
                ErrorCodes.BadRequest,
                string.Join(" ", parseResult.Errors.Select(e => e.Message))
            )
            : BuildRequest(parseResult);
    }

    private DaemonRequest BuildRequest(ParseResult parseResult) =>
        _context.TryBuildRequest(parseResult, out var request)
            ? request
            : throw new AutomationException(
                ErrorCodes.BadRequest,
                $"'{parseResult.CommandResult.Command.Name}' is not available inside the REPL."
            );
}

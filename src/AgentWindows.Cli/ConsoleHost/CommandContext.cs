using System.CommandLine;
using AgentWindows.Cli.Daemon;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli.ConsoleHost;

/// <summary>
/// Shared global options, the send/render pipeline every command goes through, and
/// the command-to-request builders the REPL reuses to translate lines into requests.
/// </summary>
public sealed class CommandContext(Option<bool> jsonOption, Option<string> sessionOption)
{
    private readonly Dictionary<Command, Func<ParseResult, DaemonRequest>> _builders = [];
    private readonly TextWriter _error = Console.Error;
    private readonly TextWriter _output = Console.Out;
    private readonly Func<string, DaemonRequest, bool, DaemonResponse> _send = Send;

    internal CommandContext(
        Option<bool> jsonOption,
        Option<string> sessionOption,
        Func<string, DaemonRequest, bool, DaemonResponse> send,
        TextWriter output,
        TextWriter error
    )
        : this(jsonOption, sessionOption)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        _send = send;
        _output = output;
        _error = error;
    }

    public Option<bool> JsonOption { get; } = jsonOption;

    public Option<string> SessionOption { get; } = sessionOption;

    public void Attach(Command command, Func<ParseResult, DaemonRequest> build)
    {
        ArgumentNullException.ThrowIfNull(command);
        _builders[command] = build;
        command.SetAction(parseResult => Execute(parseResult, build));
    }

    public string GetSession(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        return parseResult.GetValue(SessionOption) ?? PipeNames.DefaultSession;
    }

    /// <summary>Builds the request for a parsed command line; null when the parsed
    /// command has no registered builder (e.g. 'repl' or 'daemon run').</summary>
    public DaemonRequest? BuildRequest(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        return _builders.TryGetValue(parseResult.CommandResult.Command, out var build)
            ? build(parseResult)
            : null;
    }

    /// <summary>The --session value only when the caller passed it explicitly.</summary>
    public string? GetExplicitSession(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        var result = parseResult.GetResult(SessionOption);
        return result is null || result.Implicit ? null : parseResult.GetValue(SessionOption);
    }

    public int Execute(ParseResult parseResult, DaemonRequest request)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(request);
        var session = GetSession(parseResult);
        DaemonResponse response;
        try
        {
            response = _send(session, request, request is not ShutdownRequest);
        }
        catch (AutomationException ex)
        {
            response = DaemonResponse.Failure(ex.Code, ex.Message);
        }

        return Render(parseResult, response);
    }

    public int Render(ParseResult parseResult, DaemonResponse response)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(response);
        return OutputRenderer.Render(response, parseResult.GetValue(JsonOption), _output, _error);
    }

    private static DaemonResponse Send(string session, DaemonRequest request, bool spawnIfMissing)
    {
        using var client = new DaemonClient(session);
        return client.Send(request, spawnIfMissing);
    }

    private int Execute(ParseResult parseResult, Func<ParseResult, DaemonRequest> build) =>
        Execute(parseResult, build(parseResult));
}

using System.CommandLine;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli;

/// <summary>
/// Shared global options, the send/render pipeline every command goes through, and
/// the command-to-request builders the REPL reuses to translate lines into requests.
/// </summary>
public sealed class CommandContext(Option<bool> jsonOption, Option<string> sessionOption)
{
    private readonly Dictionary<Command, Func<ParseResult, DaemonRequest>> _builders = [];

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

    public bool TryBuildRequest(ParseResult parseResult, out DaemonRequest request)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        if (_builders.TryGetValue(parseResult.CommandResult.Command, out var build))
        {
            request = build(parseResult);
            return true;
        }

        request = new StatusRequest();
        return false;
    }

    private int Execute(ParseResult parseResult, Func<ParseResult, DaemonRequest> build)
    {
        var json = parseResult.GetValue(JsonOption);
        var session = GetSession(parseResult);
        DaemonResponse response;
        try
        {
            var request = build(parseResult);
            using var client = new DaemonClient(session);
            response = client.Send(request, spawnIfMissing: request is not ShutdownRequest);
        }
        catch (AutomationException ex)
        {
            response = DaemonResponse.Failure(ex.Code, ex.Message);
        }

        return OutputRenderer.Render(response, json, Console.Out, Console.Error);
    }
}

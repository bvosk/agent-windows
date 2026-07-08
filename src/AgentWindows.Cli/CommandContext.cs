using System.CommandLine;
using AgentWindows.Core.Protocol;
using AgentWindows.Core.Session;

namespace AgentWindows.Cli;

/// <summary>Shared global options plus the send/render pipeline every command goes through.</summary>
public sealed record CommandContext(Option<bool> JsonOption, Option<string> SessionOption)
{
    public void Attach(Command command, Func<ParseResult, DaemonRequest> build)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.SetAction(parseResult => Execute(parseResult, build));
    }

    public string GetSession(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        return parseResult.GetValue(SessionOption) ?? PipeNames.DefaultSession;
    }

    private int Execute(ParseResult parseResult, Func<ParseResult, DaemonRequest> build)
    {
        var json = parseResult.GetValue(JsonOption);
        var session = GetSession(parseResult);
        DaemonResponse response;
        try
        {
            var request = build(parseResult);
            var client = new DaemonClient(session);
            response = client.Send(request, spawnIfMissing: request is not ShutdownRequest);
        }
        catch (AutomationException ex)
        {
            response = DaemonResponse.Failure(ex.Code, ex.Message);
        }

        return OutputRenderer.Render(response, json, Console.Out, Console.Error);
    }
}

using System.CommandLine;
using System.IO.Pipes;
using AgentWindows.Cli.ConsoleHost;
using AgentWindows.Core.Protocol.Lifecycle;
using AgentWindows.Core.Protocol.Transport;
using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests.ConsoleHost;

public sealed class CommandContextTests
{
    [Fact]
    public async Task AttachedCommand_SendsRequestAndRendersTextResponse()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        string? sentSession = null;
        DaemonRequest? sentRequest = null;
        bool? sentSpawnIfMissing = null;
        var (root, context) = CreateContext(
            (session, request, spawnIfMissing) =>
            {
                sentSession = session;
                sentRequest = request;
                sentSpawnIfMissing = spawnIfMissing;
                return DaemonResponse.Success(new AckPayload { Detail = "ready" });
            },
            output,
            error
        );
        var command = new Command("status");
        context.Attach(command, static _ => new StatusRequest());
        root.Subcommands.Add(command);

        var exitCode = await root.Parse("status --session custom").InvokeAsync();

        exitCode.ShouldBe(0);
        sentSession.ShouldBe("custom");
        sentRequest.ShouldBeOfType<StatusRequest>();
        sentSpawnIfMissing.ShouldBe(true);
        output.ToString().ShouldBe("ok: ready\n");
        error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task AttachedShutdown_DoesNotSpawnAndCanRenderJson()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        bool? sentSpawnIfMissing = null;
        var (root, context) = CreateContext(
            (_, _, spawnIfMissing) =>
            {
                sentSpawnIfMissing = spawnIfMissing;
                return DaemonResponse.Success(new AckPayload { Detail = "stopped" });
            },
            output,
            error
        );
        var command = new Command("stop");
        context.Attach(command, static _ => new ShutdownRequest());
        root.Subcommands.Add(command);

        var exitCode = await root.Parse("stop --json").InvokeAsync();

        exitCode.ShouldBe(0);
        sentSpawnIfMissing.ShouldBe(false);
        var response = ProtocolSerializer.DeserializeResponse(output.ToString().Trim());
        response.ShouldNotBeNull().Ok.ShouldBeTrue();
        error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task AttachedCommand_ConvertsAutomationFailureToRenderedError()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var (root, context) = CreateContext(
            static (_, _, _) =>
                throw new AutomationException(ErrorCodes.Timeout, "daemon timed out"),
            output,
            error
        );
        var command = new Command("status");
        context.Attach(command, static _ => new StatusRequest());
        root.Subcommands.Add(command);

        var exitCode = await root.Parse("status").InvokeAsync();

        exitCode.ShouldBe(1);
        output.ToString().ShouldBeEmpty();
        error.ToString().ShouldContain("error [timeout]: daemon timed out");
    }

    [Fact]
    public async Task Execute_UsesProductionNamedPipeTransport()
    {
        var session = $"test-command-context-{Guid.NewGuid():N}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serverTask = ExchangeOneResponse(
            PipeNames.For(session),
            DaemonResponse.Success(new AckPayload { Detail = "stopped" }),
            timeout.Token
        );
        var root = CommandTree.Build(out var context);
        var parseResult = root.Parse($"daemon stop --session {session}");

        var executeTask = Task.Run(
            () => context.Execute(parseResult, new ShutdownRequest()),
            timeout.Token
        );
        await Task.WhenAll(serverTask, executeTask).WaitAsync(timeout.Token);

        (await executeTask).ShouldBe(0);
        (await serverTask).ShouldBeOfType<ShutdownRequest>();
    }

    [Fact]
    public void GetSession_ReturnsParsedValue()
    {
        var root = CommandTree.Build(out var context);
        var result = root.Parse("list --session custom", CommandTree.CreateConfiguration());

        context.GetSession(result).ShouldBe("custom");
    }

    [Fact]
    public void GetSession_WithoutOptionValue_ReturnsDefaultSession()
    {
        var jsonOption = new Option<bool>("--json");
        var sessionOption = new Option<string>("--session");
        var context = new CommandContext(jsonOption, sessionOption);
        var root = new RootCommand();
        root.Options.Add(sessionOption);
        var result = root.Parse("");

        context.GetSession(result).ShouldBe(PipeNames.DefaultSession);
    }

    private static (RootCommand Root, CommandContext Context) CreateContext(
        Func<string, DaemonRequest, bool, DaemonResponse> send,
        TextWriter output,
        TextWriter error
    )
    {
        var jsonOption = new Option<bool>("--json") { Recursive = true };
        var sessionOption = new Option<string>("--session") { Recursive = true };
        var context = new CommandContext(jsonOption, sessionOption, send, output, error);
        var root = new RootCommand();
        root.Options.Add(jsonOption);
        root.Options.Add(sessionOption);
        return (root, context);
    }

    private static async Task<DaemonRequest?> ExchangeOneResponse(
        string pipeName,
        DaemonResponse response,
        CancellationToken cancellationToken
    )
    {
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );
        await server.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(server, leaveOpen: true);
        using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken);
        await writer.WriteLineAsync(
            ProtocolSerializer.SerializeResponse(response).AsMemory(),
            cancellationToken
        );
        return line is null ? null : ProtocolSerializer.DeserializeRequest(line);
    }
}

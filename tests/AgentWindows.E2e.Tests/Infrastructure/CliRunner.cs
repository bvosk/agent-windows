using System.Diagnostics;
using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.E2E.Tests.Infrastructure;

/// <summary>Runs the real agent-windows executable with --json against a fixed session.</summary>
public sealed class CliRunner(string session)
{
    private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(60);

    // The CLI's spawned daemon inherits our pipe handles (bInheritHandles), so the
    // streams never reach EOF while a daemon lives. Never wait for EOF: drain
    // incrementally and snapshot what has arrived once the CLI process exits.
    private static readonly TimeSpan _outputGrace = TimeSpan.FromMilliseconds(500);

    public string Session { get; } = session;

    public Task<CliResult> RunAsync(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return RunCoreAsync(arguments);
    }

    /// <summary>
    /// Runs 'agent-windows repl', feeds the given lines to stdin, and returns the
    /// exit code plus every JSON envelope the REPL wrote.
    /// </summary>
    public Task<(int ExitCode, IReadOnlyList<DaemonResponse> Responses)> RunReplAsync(
        params string[] lines
    )
    {
        ArgumentNullException.ThrowIfNull(lines);
        return RunReplCoreAsync(lines);
    }

    private static ProcessStartInfo CreateStartInfo(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = TestPaths.CliExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static Process Start(ProcessStartInfo startInfo) =>
        Process.Start(startInfo)
        ?? throw new InvalidOperationException($"Failed to start {TestPaths.CliExecutable}");

    private static async Task WaitForExitAsync(
        Process process,
        DrainingReader stdout,
        IReadOnlyList<string> arguments
    )
    {
        using var cts = new CancellationTokenSource(_commandTimeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"agent-windows {string.Join(' ', arguments)} did not finish within "
                    + $"{_commandTimeout.TotalSeconds:0}s. stdout so far: {stdout.Snapshot()}"
            );
        }

        // The CLI wrote everything before exiting; give the pipe a moment to drain.
        await Task.Delay(_outputGrace, TimeProvider.System, CancellationToken.None);
    }

    private async Task<CliResult> RunCoreAsync(string[] arguments)
    {
        var startInfo = CreateStartInfo([.. arguments, "--session", Session, "--json"]);
        using var process = Start(startInfo);
        var stdout = new DrainingReader(process.StandardOutput);
        var stderr = new DrainingReader(process.StandardError);
        await WaitForExitAsync(process, stdout, arguments);
        var output = stdout.Snapshot();
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return new CliResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = output,
            StandardError = stderr.Snapshot(),
            Response = firstLine is null
                ? null
                : ProtocolSerializer.DeserializeResponse(firstLine.TrimEnd('\r')),
        };
    }

    private async Task<(int ExitCode, IReadOnlyList<DaemonResponse> Responses)> RunReplCoreAsync(
        string[] lines
    )
    {
        var startInfo = CreateStartInfo("repl", "--session", Session);
        startInfo.RedirectStandardInput = true;
        using var process = Start(startInfo);
        var stdout = new DrainingReader(process.StandardOutput);
        _ = new DrainingReader(process.StandardError);
        foreach (var line in lines)
        {
            await process.StandardInput.WriteLineAsync(line);
        }

        process.StandardInput.Close();
        await WaitForExitAsync(process, stdout, ["repl"]);
        var responses = stdout
            .Snapshot()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => ProtocolSerializer.DeserializeResponse(line.TrimEnd('\r')))
            .OfType<DaemonResponse>()
            .ToArray();
        return (process.ExitCode, responses);
    }
}

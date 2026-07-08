using System.Diagnostics;
using AgentWindows.Core.Protocol;
using Shouldly;
using Xunit;

namespace AgentWindows.E2e.Tests;

public sealed class ReplE2eTests
{
    [E2EFact]
    public async Task Repl_DrivesAFullFlowOverOneProcess()
    {
        var session = $"e2e-{Guid.NewGuid():N}";
        var cleanup = new CliRunner(session);
        try
        {
            var responses = await RunReplAsync(
                session,
                expectedExitCode: 0,
                "# comment lines are skipped",
                $"launch --app \"{TestPaths.TargetAppExecutable}\"",
                "snapshot -i",
                """{"cmd":"status"}""",
                "close --force",
                "exit"
            );

            responses.Count.ShouldBe(4);
            responses.ShouldAllBe(r => r.Ok);
            responses[0].Payload.ShouldBeOfType<WindowPayload>();
            var snapshot = responses[1].Payload.ShouldBeOfType<SnapshotPayload>();
            snapshot.Root.FindByAutomationId("SubmitButton").ShouldNotBeNull();
            responses[2].Payload.ShouldBeOfType<StatusPayload>();
        }
        finally
        {
            await cleanup.RunAsync("daemon", "stop");
        }
    }

    [E2EFact]
    public async Task Repl_FailsFastOnTheFirstError()
    {
        var session = $"e2e-{Guid.NewGuid():N}";
        var cleanup = new CliRunner(session);
        try
        {
            var responses = await RunReplAsync(
                session,
                expectedExitCode: 1,
                "snapshot", // no target attached -> no-target failure
                "status" // must never run
            );

            var failure = responses.ShouldHaveSingleItem();
            failure.Ok.ShouldBeFalse();
            failure.ErrorCode.ShouldBe(Core.Session.ErrorCodes.NoTarget);
        }
        finally
        {
            await cleanup.RunAsync("daemon", "stop");
        }
    }

    private static async Task<IReadOnlyList<DaemonResponse>> RunReplAsync(
        string session,
        int expectedExitCode,
        params string[] lines
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = TestPaths.CliExecutable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("repl");
        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add(session);

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {TestPaths.CliExecutable}");
        var stdout = new System.Text.StringBuilder();
        _ = PumpAsync(process.StandardOutput, stdout);
        foreach (var line in lines)
        {
            await process.StandardInput.WriteLineAsync(line);
        }

        process.StandardInput.Close();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
        process.ExitCode.ShouldBe(expectedExitCode);
        string output;
        lock (stdout)
        {
            output = stdout.ToString();
        }

        return
        [
            .. output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => ProtocolSerializer.DeserializeResponse(line.TrimEnd('\r')))
                .Where(response => response is not null)
                .Select(response => response!),
        ];
    }

    /// <summary>Drains without waiting for EOF; the spawned daemon inherits the pipe.</summary>
    private static async Task PumpAsync(StreamReader reader, System.Text.StringBuilder sink)
    {
        var chunk = new char[4096];
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(chunk);
                if (read <= 0)
                {
                    return;
                }

                lock (sink)
                {
                    sink.Append(chunk, 0, read);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Process torn down; nothing left to drain.
        }
        catch (IOException)
        {
            // Broken pipe on teardown.
        }
    }
}

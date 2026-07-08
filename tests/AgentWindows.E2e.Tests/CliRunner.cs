using System.Diagnostics;
using System.Text;
using AgentWindows.Core.Protocol;

namespace AgentWindows.E2e.Tests;

/// <summary>Runs the real agent-windows executable with --json against a fixed session.</summary>
public sealed class CliRunner(string session)
{
    private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(60);

    // The CLI's spawned daemon inherits our pipe handles (bInheritHandles), so the
    // streams never reach EOF while a daemon lives. Never wait for EOF: drain
    // incrementally and snapshot what has arrived once the CLI process exits.
    private static readonly TimeSpan _outputGrace = TimeSpan.FromMilliseconds(500);

    public string Session { get; } = session;

    public async Task<CliResult> RunAsync(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
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

        startInfo.ArgumentList.Add("--session");
        startInfo.ArgumentList.Add(Session);
        startInfo.ArgumentList.Add("--json");

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start {TestPaths.CliExecutable}");
        var stdout = new DrainingReader(process.StandardOutput);
        var stderr = new DrainingReader(process.StandardError);
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
        await Task.Delay(_outputGrace, CancellationToken.None);
        var output = stdout.Snapshot();
        var error = stderr.Snapshot();
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return new CliResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = output,
            StandardError = error,
            Response = firstLine is null
                ? null
                : ProtocolSerializer.DeserializeResponse(firstLine.TrimEnd('\r')),
        };
    }

    /// <summary>Continuously drains a reader into a buffer that can be snapshotted mid-read.</summary>
    private sealed class DrainingReader
    {
        private readonly StringBuilder _buffer = new();
        private readonly Lock _lock = new();

        public DrainingReader(StreamReader reader)
        {
            _ = DrainAsync(reader);
        }

        public string Snapshot()
        {
            lock (_lock)
            {
                return _buffer.ToString();
            }
        }

        private async Task DrainAsync(StreamReader reader)
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

                    lock (_lock)
                    {
                        _buffer.Append(chunk, 0, read);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // The process (and its streams) were disposed; nothing left to drain.
            }
            catch (IOException)
            {
                // Broken pipe on process teardown.
            }
        }
    }
}

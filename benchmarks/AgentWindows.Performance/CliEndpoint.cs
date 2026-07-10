using System.Diagnostics;
using System.Text.Json;

namespace AgentWindows.Performance;

internal sealed class CliEndpoint : IAsyncDisposable
{
    private readonly string _cliPath;
    private readonly string _coldSession;
    private readonly string _session;
    private readonly string _targetPath;
    private readonly Dictionary<string, string> _refs = new(StringComparer.Ordinal);
    private Process? _repl;

    public CliEndpoint(string cliPath, string targetPath, string label)
    {
        _cliPath = Path.GetFullPath(cliPath);
        _targetPath = Path.GetFullPath(targetPath);
        _session = $"perf-{label}-{Guid.NewGuid():N}";
        _coldSession = $"{_session}-cold";
    }

    public string Ref(string automationId) =>
        _refs.TryGetValue(automationId, out var elementRef)
            ? elementRef
            : throw new InvalidOperationException($"No ref for '{automationId}'.");

    public async Task StartAsync()
    {
        await RunOneShotAsync(["launch", "--app", _targetPath, "--timeout", "30000"])
            .ConfigureAwait(false);
        _repl = StartProcess(["repl", "--session", _session], redirect: true);
        await RefreshRefsAsync().ConfigureAwait(false);
    }

    public async Task RefreshRefsAsync()
    {
        var sample = await RunHotAsync("snapshot -i").ConfigureAwait(false);
        using var document = JsonDocument.Parse(sample.ResponseLine);
        _refs.Clear();
        IndexRefs(document.RootElement.GetProperty("payload").GetProperty("root"));
    }

    public async Task<CommandSample> RunHotAsync(string command)
    {
        var process = _repl ?? throw new InvalidOperationException("REPL is not running.");
        var started = Stopwatch.GetTimestamp();
        await process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        var line =
            await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("REPL closed before returning a response.");
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        EnsureSuccess(line);
        return new CommandSample(elapsed, line);
    }

    public async Task<CommandSample> RunColdAsync(params string[] arguments) =>
        await RunOneShotAsync(arguments, _coldSession).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        if (_repl is not null && !_repl.HasExited)
        {
            await _repl.StandardInput.WriteLineAsync("exit").ConfigureAwait(false);
            await _repl.WaitForExitAsync().ConfigureAwait(false);
            _repl.Dispose();
        }

        await IgnoreFailureAsync(["close", "--force"]).ConfigureAwait(false);
        await IgnoreFailureAsync(["daemon", "stop"]).ConfigureAwait(false);
        await IgnoreFailureAsync(["daemon", "stop"], _coldSession).ConfigureAwait(false);
    }

    private async Task<CommandSample> RunOneShotAsync(
        IReadOnlyList<string> arguments,
        string? session = null
    )
    {
        using var process = StartProcess(
            [.. arguments, "--session", session ?? _session, "--json"],
            true
        );
        var started = Stopwatch.GetTimestamp();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var output = (await outputTask.ConfigureAwait(false)).Trim();
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{Path.GetFileName(_cliPath)} {string.Join(' ', arguments)}' failed: {error} {output}"
            );
        }

        EnsureSuccess(output);
        return new CommandSample(elapsed, output);
    }

    private Process StartProcess(IReadOnlyList<string> arguments, bool redirect)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _cliPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = redirect,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{_cliPath}'.");
    }

    private async Task IgnoreFailureAsync(IReadOnlyList<string> arguments, string? session = null)
    {
        try
        {
            await RunOneShotAsync(arguments, session).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Best-effort cleanup.
        }
    }

    private static void EnsureSuccess(string responseLine)
    {
        using var document = JsonDocument.Parse(responseLine);
        if (!document.RootElement.GetProperty("ok").GetBoolean())
        {
            throw new InvalidOperationException($"CLI returned failure: {responseLine}");
        }
    }

    private void IndexRefs(JsonElement node)
    {
        if (
            node.TryGetProperty("automationId", out var automationId)
            && automationId.ValueKind == JsonValueKind.String
            && node.TryGetProperty("ref", out var elementRef)
            && elementRef.ValueKind == JsonValueKind.String
        )
        {
            _refs[automationId.GetString()!] = elementRef.GetString()!;
        }

        if (node.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                IndexRefs(child);
            }
        }
    }
}

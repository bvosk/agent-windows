using System.Diagnostics;
using Xunit;

namespace AgentWindows.E2e.Tests;

/// <summary>
/// Prevents separate E2E test processes from driving the shared desktop concurrently.
/// </summary>
public sealed class DesktopAutomationLock : IAsyncLifetime
{
    private static readonly string _lockPath = Path.Combine(
        Path.GetTempPath(),
        "agent-windows-e2e-desktop.lock"
    );
    private static readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);

    private FileStream? _lockHandle;

    public async ValueTask InitializeAsync()
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable("AGENT_WINDOWS_E2E"),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        IOException? lastException = null;
        while (stopwatch.Elapsed < _timeout)
        {
            try
            {
                _lockHandle = new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.DeleteOnClose
                );
                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
                await Task.Delay(_retryDelay, TimeProvider.System, CancellationToken.None);
            }
        }

        throw new TimeoutException(
            "Another agent-windows E2E run held the desktop lock for more than "
                + $"{_timeout.TotalMinutes:0} minutes.",
            lastException
        );
    }

    public ValueTask DisposeAsync()
    {
        _lockHandle?.Dispose();
        return ValueTask.CompletedTask;
    }
}

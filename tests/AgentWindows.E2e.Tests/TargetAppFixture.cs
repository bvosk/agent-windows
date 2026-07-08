using System.Diagnostics;
using AgentWindows.Core.Protocol;
using Xunit;

namespace AgentWindows.E2e.Tests;

/// <summary>
/// Launches the bundled WPF target app through the CLI in a unique daemon session,
/// and tears both down after the test class finishes.
/// </summary>
public sealed class TargetAppFixture : IAsyncLifetime
{
    public CliRunner Cli { get; } = new($"e2e-{Guid.NewGuid():N}");

    public int TargetProcessId { get; private set; }

    public async ValueTask InitializeAsync()
    {
        var result = await Cli.RunAsync("launch", "--app", TestPaths.TargetAppExecutable);
        TargetProcessId = result.ShouldSucceedWith<WindowPayload>().Window.ProcessId;
    }

    public async ValueTask DisposeAsync()
    {
        await Cli.RunAsync("close", "--force");
        await Cli.RunAsync("daemon", "stop");
        KillTargetIfAlive();
    }

    /// <summary>Takes a fresh interactive snapshot and returns the ref of the element.</summary>
    public async Task<string> RefOfAsync(string automationId)
    {
        var snapshot = await SnapshotAsync("-i");
        return snapshot.Root.RequireByAutomationId(automationId).RequireRef();
    }

    /// <summary>Takes a fresh interactive snapshot and returns the element's states.</summary>
    public async Task<IReadOnlyList<string>> StatesOfAsync(string automationId)
    {
        var snapshot = await SnapshotAsync("-i");
        return snapshot.Root.RequireByAutomationId(automationId).States;
    }

    public async Task<SnapshotPayload> SnapshotAsync(params string[] extraArguments)
    {
        string[] arguments = ["snapshot", .. extraArguments];
        var result = await Cli.RunAsync(arguments);
        return result.ShouldSucceedWith<SnapshotPayload>();
    }

    private void KillTargetIfAlive()
    {
        if (TargetProcessId == 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(TargetProcessId);
            process.Kill();
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}

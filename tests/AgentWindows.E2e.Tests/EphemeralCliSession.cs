namespace AgentWindows.E2e.Tests;

/// <summary>
/// A CliRunner on a unique throwaway session whose daemon is stopped on dispose,
/// replacing hand-written try/finally blocks in tests.
/// </summary>
public sealed class EphemeralCliSession : IAsyncDisposable
{
    public CliRunner Cli { get; } = new($"e2e-{Guid.NewGuid():N}");

    public async ValueTask DisposeAsync() => await Cli.RunAsync("daemon", "stop");
}

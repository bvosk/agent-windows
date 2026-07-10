namespace AgentWindows.Cli.Daemon;

internal interface IDaemonConnection : IDisposable
{
    public string? ReadLine();

    public void WriteLine(string line);
}

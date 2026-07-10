namespace AgentWindows.Cli;

internal interface IDaemonConnection : IDisposable
{
    public string? ReadLine();

    public void WriteLine(string line);
}

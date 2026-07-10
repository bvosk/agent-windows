namespace AgentWindows.Core.Protocol.Transport;

public static class PipeNames
{
    public const string DefaultSession = "default";
    public const string Prefix = "agent-windows.";

    public static string For(string session) => $"{Prefix}{session}";
}

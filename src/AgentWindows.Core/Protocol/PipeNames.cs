namespace AgentWindows.Core.Protocol;

public static class PipeNames
{
    public const string DefaultSession = "default";

    public static string For(string session) => $"agent-windows.{session}";
}

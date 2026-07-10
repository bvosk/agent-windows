using AgentWindows.Core.Windows;

namespace AgentWindows.Core.Session;

public sealed record SessionStatus
{
    public required int DaemonProcessId { get; init; }

    public WindowInfo? Target { get; init; }

    public required int SnapshotGeneration { get; init; }

    public required int RefCount { get; init; }
}

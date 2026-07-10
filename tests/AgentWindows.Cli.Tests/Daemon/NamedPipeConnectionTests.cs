using AgentWindows.Cli.Daemon;
using Shouldly;
using Xunit;

namespace AgentWindows.Cli.Tests.Daemon;

public sealed class NamedPipeConnectionTests
{
    [Fact]
    public void MissingPipe_ConnectTimesOut()
    {
        var pipeName = $"agent-windows-test-connection-missing-{Guid.NewGuid():N}";

        Should.Throw<TimeoutException>(() => NamedPipeConnection.Connect(pipeName, 0));
    }
}

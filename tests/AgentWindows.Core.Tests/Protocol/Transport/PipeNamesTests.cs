using AgentWindows.Core.Protocol.Transport;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests.Protocol.Transport;

public sealed class PipeNamesTests
{
    [Fact]
    public void For_PrefixesSessionName() =>
        PipeNames.For("research").ShouldBe("agent-windows.research");
}

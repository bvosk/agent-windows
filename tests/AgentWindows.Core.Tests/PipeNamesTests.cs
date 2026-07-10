using AgentWindows.Core.Protocol;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests;

public sealed class PipeNamesTests
{
    [Fact]
    public void For_PrefixesSessionName() =>
        PipeNames.For("research").ShouldBe("agent-windows.research");
}

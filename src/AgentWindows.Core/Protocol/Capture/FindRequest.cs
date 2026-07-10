using AgentWindows.Core.Elements;
using AgentWindows.Core.Protocol.Transport;

namespace AgentWindows.Core.Protocol.Capture;

public sealed record FindRequest : DaemonRequest
{
    public required ElementSelector Selector { get; init; }

    public bool All { get; init; }
}

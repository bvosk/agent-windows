namespace AgentWindows.Core.Protocol;

public sealed record DaemonResponse
{
    public required bool Ok { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    public ResponsePayload? Payload { get; init; }

    public static DaemonResponse Success(ResponsePayload payload) =>
        new() { Ok = true, Payload = payload };

    public static DaemonResponse Failure(string errorCode, string message) =>
        new()
        {
            Ok = false,
            ErrorCode = errorCode,
            Message = message,
        };
}

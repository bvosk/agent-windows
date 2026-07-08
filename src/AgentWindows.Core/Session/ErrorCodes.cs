namespace AgentWindows.Core.Session;

public static class ErrorCodes
{
    public const string BadRequest = "bad-request";
    public const string NoTarget = "no-target";
    public const string NotFound = "not-found";
    public const string UnknownRef = "unknown-ref";
    public const string StaleRef = "stale-ref";
    public const string Timeout = "timeout";
    public const string ElevatedTarget = "elevated-target";
    public const string LaunchFailed = "launch-failed";
    public const string PatternUnsupported = "pattern-unsupported";
    public const string InternalError = "internal-error";
}

namespace AgentWindows.Core.Session;

public sealed class AutomationException : Exception
{
    public AutomationException()
        : this(ErrorCodes.InternalError, "An automation error occurred.") { }

    public AutomationException(string message)
        : this(ErrorCodes.InternalError, message) { }

    public AutomationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = ErrorCodes.InternalError;
    }

    public AutomationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public AutomationException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

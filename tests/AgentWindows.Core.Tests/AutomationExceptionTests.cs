using AgentWindows.Core.Session;
using Shouldly;
using Xunit;

namespace AgentWindows.Core.Tests;

public sealed class AutomationExceptionTests
{
    [Fact]
    public void Constructors_PreserveErrorDetails()
    {
        var innerException = new InvalidOperationException("inner");

        var defaultException = new AutomationException();
        var messageException = new AutomationException("message");
        var messageAndInnerException = new AutomationException("message", innerException);
        var codedException = new AutomationException("custom-code", "message");
        var completeException = new AutomationException("custom-code", "message", innerException);

        defaultException.Code.ShouldBe(ErrorCodes.InternalError);
        defaultException.Message.ShouldBe("An automation error occurred.");

        messageException.Code.ShouldBe(ErrorCodes.InternalError);
        messageException.Message.ShouldBe("message");

        messageAndInnerException.Code.ShouldBe(ErrorCodes.InternalError);
        messageAndInnerException.Message.ShouldBe("message");
        messageAndInnerException.InnerException.ShouldBeSameAs(innerException);

        codedException.Code.ShouldBe("custom-code");
        codedException.Message.ShouldBe("message");

        completeException.Code.ShouldBe("custom-code");
        completeException.Message.ShouldBe("message");
        completeException.InnerException.ShouldBeSameAs(innerException);
    }
}

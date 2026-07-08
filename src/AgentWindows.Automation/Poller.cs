using System.Diagnostics;
using AgentWindows.Core.Session;

namespace AgentWindows.Automation;

public static class Poller
{
    private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(150);

    /// <summary>Polls until <paramref name="condition"/> is true or the timeout elapses.</summary>
    public static void WaitUntil(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (condition())
            {
                return;
            }

            if (stopwatch.Elapsed >= timeout)
            {
                throw new AutomationException(ErrorCodes.Timeout, timeoutMessage);
            }

            Thread.Sleep(_interval);
        }
    }
}

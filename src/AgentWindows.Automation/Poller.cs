using System.Diagnostics;
using AgentWindows.Core.Session;

namespace AgentWindows.Automation;

public static class Poller
{
    // Back off exponentially: conditions that settle within a few milliseconds
    // should not pay a flat long interval.
    private static readonly TimeSpan _initialInterval = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan _maxInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>Polls until <paramref name="condition"/> is true or the timeout elapses.</summary>
    public static void WaitUntil(Func<bool> condition, TimeSpan timeout, string timeoutMessage)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var stopwatch = Stopwatch.StartNew();
        var interval = _initialInterval;
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

            Thread.Sleep(interval);
            interval = interval >= _maxInterval ? _maxInterval : interval * 2;
        }
    }
}

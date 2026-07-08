using System.Runtime.CompilerServices;
using Xunit;

namespace AgentWindows.E2e.Tests;

/// <summary>A fact that only runs when AGENT_WINDOWS_E2E=1 (set by 'mise run e2e' and CI).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1
    )
        : base(sourceFilePath, sourceLineNumber)
    {
        if (
            !string.Equals(
                Environment.GetEnvironmentVariable("AGENT_WINDOWS_E2E"),
                "1",
                StringComparison.Ordinal
            )
        )
        {
            Skip = "Set AGENT_WINDOWS_E2E=1 to run e2e tests (they drive a real desktop UI).";
        }
    }
}

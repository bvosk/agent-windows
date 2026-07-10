using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using AgentWindows.Automation.Session;
using AgentWindows.Cli.ConsoleHost;
using AgentWindows.Core.Snapshots;
using Shouldly;
using Xunit;

namespace AgentWindows.Architecture.Tests;

public sealed class CoverageExclusionTests
{
    private const string _excludedAssemblies =
        "[coverlet.*]*,[xunit.*]*,[Microsoft.Testing.*]*,[Microsoft.Testplatform.*]*,"
        + "[Microsoft.VisualStudio.TestPlatform.*]*,[MSTest*]*,[testhost*]*,"
        + "[AgentWindows.Automation]*";
    private const string _excludedAttributes =
        "ExcludeFromCodeCoverage,ExcludeFromCodeCoverageAttribute,GeneratedCodeAttribute,"
        + "CompilerGeneratedAttribute";
    private const string _includedAssemblies = "[*]*";
    private static readonly IReadOnlyDictionary<string, string> _approvedExclusions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method AgentWindows.Cli.Daemon.DaemonHost.Run(System.String)"] =
                "Windows/FlaUI and native named-pipe composition root; validated by "
                + "daemon-lifecycle end-to-end tests.",
            ["method AgentWindows.Cli.Daemon.DaemonHost.CreateNamedPipeServer(System.String)"] =
                "Native named-pipe factory is exercised by daemon-lifecycle end-to-end tests; "
                + "creation policy is unit tested through CreateServer.",
            ["method AgentWindows.Cli.Daemon.DaemonManager.StopAll()"] =
                "Process-wide composition wrapper; orchestration is covered through the "
                + "injectable overload and production wiring is exercised by the reinstall "
                + "smoke path.",
            ["method AgentWindows.Cli.Daemon.DaemonManager.StopSession(System.String)"] =
                "Real daemon-client transport is covered by daemon-lifecycle end-to-end tests; "
                + "stop-all orchestration uses the injected delegate in unit tests.",
        };
    private static readonly Assembly[] _productionAssemblies =
    [
        typeof(UiNode).Assembly,
        typeof(FlaUiSession).Assembly,
        typeof(CommandContext).Assembly,
    ];

    [Fact]
    public void ProductionCoverageExclusions_AreExactlyAllowlisted()
    {
        var actual = _productionAssemblies
            .SelectMany(FindExclusions)
            .ToDictionary(item => item.Id, item => item.Justification, StringComparer.Ordinal);

        actual
            .Keys.Order(StringComparer.Ordinal)
            .ShouldBe(_approvedExclusions.Keys.Order(StringComparer.Ordinal));
        foreach (var approved in _approvedExclusions)
        {
            actual[approved.Key].ShouldBe(approved.Value);
        }
    }

    [Fact]
    public void CoverageConfiguration_HasAnExplicitGovernedScope()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "testconfig.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var coverlet = document.RootElement.GetProperty("platformOptions").GetProperty("Coverlet");

        coverlet.GetProperty("include").GetString().ShouldBe(_includedAssemblies);
        coverlet.GetProperty("exclude").GetString().ShouldBe(_excludedAssemblies);
        coverlet.GetProperty("excludeByAttribute").GetString().ShouldBe(_excludedAttributes);
        coverlet.GetProperty("excludeByFile").GetString().ShouldBe("**/obj/**/*.cs");
    }

    private static IEnumerable<(string Id, string? Justification)> FindExclusions(Assembly assembly)
    {
        if (
            assembly.GetCustomAttribute<ExcludeFromCodeCoverageAttribute>() is { } assemblyAttribute
        )
        {
            yield return ($"assembly {assembly.GetName().Name}", assemblyAttribute.Justification);
        }

        foreach (var type in assembly.GetTypes())
        {
            if (
                type.FullName?.StartsWith(
                    "Coverlet.Core.Instrumentation.Tracker.",
                    StringComparison.Ordinal
                ) == true
            )
            {
                continue;
            }

            if (type.GetCustomAttribute<ExcludeFromCodeCoverageAttribute>() is { } typeAttribute)
            {
                yield return ($"type {type.FullName}", typeAttribute.Justification);
            }

            foreach (
                var member in type.GetMembers(
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.Static
                        | BindingFlags.DeclaredOnly
                )
            )
            {
                if (
                    member.GetCustomAttribute<ExcludeFromCodeCoverageAttribute>() is
                    { } memberAttribute
                )
                {
                    yield return (MemberId(member), memberAttribute.Justification);
                }
            }
        }
    }

    private static string MemberId(MemberInfo member) =>
        member switch
        {
            MethodBase method => $"method {method.DeclaringType!.FullName}.{method.Name}"
                + $"({string.Join(",", method.GetParameters().Select(ParameterTypeName))})",
            PropertyInfo property => $"property {property.DeclaringType!.FullName}.{property.Name}",
            EventInfo eventInfo => $"event {eventInfo.DeclaringType!.FullName}.{eventInfo.Name}",
            _ => $"member {member.DeclaringType!.FullName}.{member.Name}",
        };

    private static string ParameterTypeName(ParameterInfo parameter) =>
        parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
}

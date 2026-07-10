using System.Reflection;
using System.Text;

namespace AgentWindows.Cli;

/// <summary>
/// Serves the agent-facing skill documentation bundled into the CLI as embedded
/// resources named 'skill-data/&lt;skill&gt;/...' (see the csproj glob).
/// </summary>
public static class SkillCatalog
{
    private const string _resourcePrefix = "skill-data/";
    private const string _skillFileName = "SKILL.md";
    private const string _referencesSegment = "references/";

    /// <summary>
    /// Manifest resource names keyed by their separator-normalized path;
    /// MSBuild's %(RecursiveDir) uses backslashes on Windows.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> _resources = typeof(SkillCatalog)
        .Assembly.GetManifestResourceNames()
        .Where(name =>
            name.Replace('\\', '/').StartsWith(_resourcePrefix, StringComparison.Ordinal)
        )
        .ToDictionary(name => name.Replace('\\', '/'), name => name, StringComparer.Ordinal);

    public static IReadOnlyList<string> SkillNames { get; } =
    [
        .. _resources
            .Keys.Where(path =>
                path.EndsWith($"/{_skillFileName}", StringComparison.Ordinal)
                && path.Count(c => c == '/') == 2
            )
            .Select(path => path.Split('/')[1])
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>The sole bundled skill's name, or null when zero or several are bundled.</summary>
    public static string? DefaultSkillName { get; } = SkillNames.Count == 1 ? SkillNames[0] : null;

    /// <summary>
    /// Returns the skill body as markdown, with the reference files appended when
    /// <paramref name="full"/> is set, or null when the skill does not exist.
    /// </summary>
    public static string? Read(string name, bool full)
    {
        var skillDirectory = $"{_resourcePrefix}{name}/";
        var skillPath = skillDirectory + _skillFileName;
        if (!_resources.ContainsKey(skillPath))
        {
            return null;
        }

        var builder = new StringBuilder(ReadResource(skillPath));
        if (!full)
        {
            return builder.ToString();
        }

        var referencePaths = _resources
            .Keys.Where(path =>
                path.StartsWith(skillDirectory + _referencesSegment, StringComparison.Ordinal)
            )
            .Order(StringComparer.Ordinal);
        foreach (var path in referencePaths)
        {
            builder
                .Append("\n\n---\n\n<!-- ")
                .Append(path.AsSpan(skillDirectory.Length))
                .Append(" -->\n\n")
                .Append(ReadResource(path));
        }

        return builder.ToString();
    }

    private static string ReadResource(string normalizedPath)
    {
        using var stream = typeof(SkillCatalog).Assembly.GetManifestResourceStream(
            _resources[normalizedPath]
        )!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

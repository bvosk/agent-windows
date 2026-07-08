namespace AgentWindows.E2e.Tests;

public static class TestPaths
{
#if DEBUG
    private const string _configuration = "Debug";
#else
    private const string _configuration = "Release";
#endif

    private static readonly Lazy<string> _repoRoot = new(FindRepoRoot);

    public static string CliExecutable =>
        Path.Combine(
            _repoRoot.Value,
            "src",
            "AgentWindows.Cli",
            "bin",
            _configuration,
            "net10.0-windows",
            "agent-windows.exe"
        );

    public static string TargetAppExecutable =>
        Path.Combine(
            _repoRoot.Value,
            "tests",
            "AgentWindows.E2eTarget",
            "bin",
            _configuration,
            "net10.0-windows",
            "AgentWindows.E2eTarget.exe"
        );

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentWindows.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root (AgentWindows.slnx) above "
                + AppContext.BaseDirectory
        );
    }
}

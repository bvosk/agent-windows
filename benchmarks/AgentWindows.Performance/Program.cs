using System.Runtime.InteropServices;

namespace AgentWindows.Performance;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Arguments.Parse(args);
            await using var candidate = new CliEndpoint(
                options.Candidate,
                options.Target,
                "candidate"
            );
            var targetProcessId = await candidate.StartAsync().ConfigureAwait(false);
            CliEndpoint? reference = null;
            if (options.Reference is not null)
            {
                reference = new CliEndpoint(options.Reference, options.Target, "reference");
                await reference.StartAsync(targetProcessId).ConfigureAwait(false);
            }

            try
            {
                var runner = new BenchmarkRunner(
                    candidate,
                    reference,
                    options.Warmups,
                    options.Samples
                );
                if (options.PreflightOnly)
                {
                    await runner.PreflightAsync().ConfigureAwait(false);
                    Console.WriteLine("Performance preflight passed.");
                    return 0;
                }

                var results = await runner.RunAsync().ConfigureAwait(false);
                var run = new BenchmarkRun
                {
                    SuiteVersion = options.SuiteVersion,
                    Context = options.Context,
                    CandidateSha = options.CandidateSha,
                    ReferenceSha = options.ReferenceSha,
                    Timestamp = DateTimeOffset.UtcNow,
                    Testbed = CaptureTestbed(),
                    Benchmarks = results,
                };
                await ReportWriter.WriteAsync(run, options.Output).ConfigureAwait(false);
            }
            finally
            {
                if (reference is not null)
                {
                    await reference.DisposeAsync().ConfigureAwait(false);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static TestbedInfo CaptureTestbed() =>
        new()
        {
            RunnerImage = Environment.GetEnvironmentVariable("ImageOS") ?? "local-windows",
            RunnerImageVersion = Environment.GetEnvironmentVariable("ImageVersion") ?? "local",
            Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown",
            OperatingSystem = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            AvailableMemoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        };

    private sealed record Arguments
    {
        public required string Candidate { get; init; }

        public string? Reference { get; init; }

        public required string Target { get; init; }

        public required string Output { get; init; }

        public required string CandidateSha { get; init; }

        public string? ReferenceSha { get; init; }

        public required string Context { get; init; }

        public required string SuiteVersion { get; init; }

        public required int Warmups { get; init; }

        public required int Samples { get; init; }

        public required bool PreflightOnly { get; init; }

        public static Arguments Parse(IReadOnlyList<string> args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var quick = false;
            var preflightOnly = false;
            for (var index = 0; index < args.Count; index++)
            {
                if (args[index] == "--quick")
                {
                    quick = true;
                    continue;
                }

                if (args[index] == "--preflight-only")
                {
                    preflightOnly = true;
                    continue;
                }

                if (
                    !args[index].StartsWith("--", StringComparison.Ordinal)
                    || index + 1 >= args.Count
                )
                {
                    throw new ArgumentException($"Invalid argument '{args[index]}'.");
                }

                values[args[index]] = args[++index];
            }

            return new Arguments
            {
                Candidate = Required(values, "--candidate"),
                Reference = Optional(values, "--reference"),
                Target = Required(values, "--target"),
                Output = Optional(values, "--output") ?? "artifacts/performance",
                CandidateSha = Optional(values, "--candidate-sha") ?? "working-tree",
                ReferenceSha = Optional(values, "--reference-sha"),
                Context = Optional(values, "--context") ?? "local",
                SuiteVersion = Optional(values, "--suite-version") ?? "v1",
                Warmups = ParseInt(values, "--warmups", quick ? 2 : 5),
                Samples = ParseInt(values, "--samples", quick ? 5 : 30),
                PreflightOnly = preflightOnly,
            };
        }

        private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
            Optional(values, name)
            ?? throw new ArgumentException($"Missing required option {name}.");

        private static string? Optional(IReadOnlyDictionary<string, string> values, string name) =>
            values.TryGetValue(name, out var value) ? value : null;

        private static int ParseInt(
            IReadOnlyDictionary<string, string> values,
            string name,
            int fallback
        ) =>
            !values.TryGetValue(name, out var value) ? fallback
            : int.TryParse(value, out var parsed) && parsed > 0 ? parsed
            : throw new ArgumentException($"{name} must be a positive integer.");
    }
}

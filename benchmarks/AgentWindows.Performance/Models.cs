namespace AgentWindows.Performance;

internal sealed record BenchmarkRun
{
    public int SchemaVersion { get; init; } = 1;

    public required string SuiteVersion { get; init; }

    public required string Context { get; init; }

    public required string CandidateSha { get; init; }

    public string? ReferenceSha { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required TestbedInfo Testbed { get; init; }

    public required IReadOnlyList<BenchmarkResult> Benchmarks { get; init; }

    public string Status { get; init; } = "success";

    public int RetryCount { get; init; }

    public int RejectedSamples { get; init; }

    public IReadOnlyList<string> CorrectnessFailures { get; init; } = [];
}

internal sealed record TestbedInfo
{
    public required string RunnerImage { get; init; }

    public required string RunnerImageVersion { get; init; }

    public required string Processor { get; init; }

    public required string OperatingSystem { get; init; }

    public required string Runtime { get; init; }

    public required string Architecture { get; init; }

    public required long AvailableMemoryBytes { get; init; }
}

internal sealed record BenchmarkResult
{
    public required string Id { get; init; }

    public required string Category { get; init; }

    public required string Unit { get; init; }

    public required Measurement Candidate { get; init; }

    public Measurement? Reference { get; init; }

    public double? Score { get; init; }

    public bool CorrectnessPassed { get; init; } = true;

    public int RejectedSamples { get; init; }

    public IReadOnlyList<string> Advisories { get; init; } = [];
}

internal sealed record Measurement
{
    public required double Median { get; init; }

    public required double P95 { get; init; }

    public required double Mean { get; init; }

    public required double StandardDeviation { get; init; }

    public required int SampleCount { get; init; }

    public required IReadOnlyList<double> Samples { get; init; }
}

internal readonly record struct CommandSample(double ElapsedMilliseconds, string ResponseLine);

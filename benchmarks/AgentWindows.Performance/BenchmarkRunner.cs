namespace AgentWindows.Performance;

internal sealed class BenchmarkRunner(
    CliEndpoint candidate,
    CliEndpoint? reference,
    int warmups,
    int samples
)
{
    private readonly CliEndpoint _candidate = candidate;
    private readonly CliEndpoint? _reference = reference;
    private readonly int _samples = samples;
    private readonly int _warmups = warmups;

    public async Task PreflightAsync()
    {
        foreach (var scenario in CreateScenarios())
        {
            await PreflightEndpointAsync(scenario, _candidate, candidate: true)
                .ConfigureAwait(false);
            if (_reference is not null)
            {
                await PreflightEndpointAsync(scenario, _reference, candidate: false)
                    .ConfigureAwait(false);
            }
        }
    }

    public async Task<IReadOnlyList<BenchmarkResult>> RunAsync()
    {
        var scenarios = CreateScenarios();
        var results = new List<BenchmarkResult>();
        foreach (var scenario in scenarios)
        {
            await scenario.Prepare(_candidate).ConfigureAwait(false);
            if (_reference is not null)
            {
                await scenario.Prepare(_reference).ConfigureAwait(false);
            }

            results.Add(await MeasureAsync(scenario).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<BenchmarkResult> MeasureAsync(Scenario scenario)
    {
        for (var i = 0; i < _warmups; i++)
        {
            if (_reference is not null)
            {
                await scenario.Reference(_reference).ConfigureAwait(false);
            }

            await scenario.Candidate(_candidate).ConfigureAwait(false);
        }

        var candidateSamples = new List<double>();
        var referenceSamples = new List<double>();
        var targetSamples = scenario.Slow ? Math.Max(5, _samples / 2) : _samples;
        while (candidateSamples.Count < targetSamples)
        {
            if (_reference is not null && referenceSamples.Count < targetSamples)
            {
                referenceSamples.Add(
                    (await scenario.Reference(_reference).ConfigureAwait(false)).ElapsedMilliseconds
                );
            }

            candidateSamples.Add(
                (await scenario.Candidate(_candidate).ConfigureAwait(false)).ElapsedMilliseconds
            );
            if (candidateSamples.Count < targetSamples)
            {
                candidateSamples.Add(
                    (await scenario.Candidate(_candidate).ConfigureAwait(false)).ElapsedMilliseconds
                );
            }

            if (_reference is not null && referenceSamples.Count < targetSamples)
            {
                referenceSamples.Add(
                    (await scenario.Reference(_reference).ConfigureAwait(false)).ElapsedMilliseconds
                );
            }
        }

        var candidate = Statistics.Calculate(candidateSamples);
        var reference = referenceSamples.Count == 0 ? null : Statistics.Calculate(referenceSamples);
        var advisories = BuildAdvisories(candidate, reference);
        return new BenchmarkResult
        {
            Id = scenario.Id,
            Category = scenario.Category,
            Unit = "ms",
            Candidate = candidate,
            Reference = reference,
            Score = reference is null ? null : (candidate.Median / reference.Median) * 100,
            Advisories = advisories,
        };
    }

    private static async Task PreflightEndpointAsync(
        Scenario scenario,
        CliEndpoint endpoint,
        bool candidate
    )
    {
        var endpointName = candidate ? "candidate" : "reference";
        try
        {
            await scenario.Prepare(endpoint).ConfigureAwait(false);
            _ = candidate
                ? await scenario.Candidate(endpoint).ConfigureAwait(false)
                : await scenario.Reference(endpoint).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Performance preflight failed for {endpointName} scenario '{scenario.Id}'.",
                ex
            );
        }
    }

    private static IReadOnlyList<string> BuildAdvisories(
        Measurement candidate,
        Measurement? reference
    )
    {
        var advisories = new List<string>();
        if (
            reference is not null
            && candidate.Median - reference.Median > 5
            && candidate.Median / reference.Median > 1.15
        )
        {
            advisories.Add("median-regression");
        }

        if (reference is not null && candidate.P95 / reference.P95 > 1.25)
        {
            advisories.Add("p95-regression");
        }

        if (candidate.Mean > 0 && candidate.StandardDeviation / candidate.Mean > 0.15)
        {
            advisories.Add("high-variation");
        }

        return advisories;
    }

    private static IReadOnlyList<Scenario> CreateScenarios() =>
        [
            new(
                "client.status",
                "Client/Transport",
                Slow: false,
                NoPreparation,
                endpoint => endpoint.RunColdAsync("status"),
                endpoint => endpoint.RunColdAsync("status")
            ),
            new(
                "daemon.status",
                "Client/Transport",
                Slow: false,
                NoPreparation,
                endpoint => endpoint.RunHotAsync("status"),
                endpoint => endpoint.RunHotAsync("status")
            ),
            new(
                "snapshots.interactive",
                "Snapshots",
                Slow: true,
                NoPreparation,
                endpoint => endpoint.RunHotAsync("snapshot -i"),
                endpoint => endpoint.RunHotAsync("snapshot -i")
            ),
            new(
                "snapshots.raw",
                "Snapshots",
                Slow: true,
                NoPreparation,
                endpoint => endpoint.RunHotAsync("snapshot"),
                endpoint => endpoint.RunHotAsync("snapshot")
            ),
            new(
                "discovery.exact-id",
                "Discovery",
                Slow: true,
                NoPreparation,
                endpoint => endpoint.RunHotAsync("find --automation-id SubmitButton"),
                endpoint => endpoint.RunHotAsync("snapshot -i")
            ),
            new(
                "actions.activate",
                "Actions",
                Slow: false,
                endpoint => endpoint.RefreshRefsAsync(),
                endpoint => endpoint.RunHotAsync($"activate @{endpoint.Ref("SubmitButton")}"),
                endpoint => endpoint.RunHotAsync($"click @{endpoint.Ref("SubmitButton")}")
            ),
            new(
                "actions.repeated-click",
                "Actions",
                Slow: false,
                endpoint => endpoint.RefreshRefsAsync(),
                endpoint => endpoint.RunHotAsync($"click @{endpoint.Ref("SubmitButton")}"),
                endpoint => endpoint.RunHotAsync($"click @{endpoint.Ref("SubmitButton")}")
            ),
            new(
                "actions.fill",
                "Actions",
                Slow: false,
                endpoint => endpoint.RefreshRefsAsync(),
                endpoint => endpoint.RunHotAsync($"fill @{endpoint.Ref("InputBox")} perf"),
                endpoint => endpoint.RunHotAsync($"fill @{endpoint.Ref("InputBox")} perf")
            ),
            new(
                "actions.select",
                "Actions",
                Slow: false,
                endpoint => endpoint.RefreshRefsAsync(),
                endpoint => endpoint.RunHotAsync($"select @{endpoint.Ref("ColorCombo")} Green"),
                endpoint => endpoint.RunHotAsync($"select @{endpoint.Ref("ColorCombo")} Green")
            ),
        ];

    private static Task NoPreparation(CliEndpoint _) => Task.CompletedTask;

    private sealed record Scenario(
        string Id,
        string Category,
        bool Slow,
        Func<CliEndpoint, Task> Prepare,
        Func<CliEndpoint, Task<CommandSample>> Candidate,
        Func<CliEndpoint, Task<CommandSample>> Reference
    );
}

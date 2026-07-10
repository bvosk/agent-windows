using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace AgentWindows.Performance;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task WriteAsync(BenchmarkRun run, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteJsonAsync(Path.Combine(outputDirectory, "run.json"), run).ConfigureAwait(false);
        await WriteJsonAsync(
                Path.Combine(outputDirectory, "samples.json"),
                run.Benchmarks.Select(result => new
                {
                    result.Id,
                    Candidate = result.Candidate.Samples,
                    Reference = result.Reference?.Samples,
                })
            )
            .ConfigureAwait(false);
        await WriteJsonAsync(
                Path.Combine(outputDirectory, "github-action-benchmark.json"),
                run.Benchmarks.Select(result => new
                {
                    name = result.Id,
                    unit = result.Unit,
                    value = result.Candidate.Median,
                    range = result.Candidate.StandardDeviation,
                    extra = result.Score is null
                        ? $"candidate {run.CandidateSha}"
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"score {result.Score:0.0}; reference {run.ReferenceSha}; candidate {run.CandidateSha}"
                        ),
                })
            )
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "summary.md"),
                BuildMarkdown(run)
            )
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "summary.svg"), BuildSvg(run))
            .ConfigureAwait(false);
    }

    private static Task WriteJsonAsync<T>(string path, T value) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, _jsonOptions));

    private static string BuildMarkdown(BenchmarkRun run)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## Performance comparison");
        builder.AppendLine();
        builder.AppendLine(
            "| Benchmark | Candidate p50 | Candidate p95 | Reference p50 | Score | Advisory |"
        );
        builder.AppendLine("| --- | ---: | ---: | ---: | ---: | --- |");
        foreach (var result in run.Benchmarks)
        {
            builder
                .Append("| ")
                .Append(result.Id)
                .Append(" | ")
                .AppendFormat(CultureInfo.InvariantCulture, "{0:0.00} ms", result.Candidate.Median)
                .Append(" | ")
                .AppendFormat(CultureInfo.InvariantCulture, "{0:0.00} ms", result.Candidate.P95)
                .Append(" | ")
                .Append(
                    result.Reference is null
                        ? "—"
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"{result.Reference.Median:0.00} ms"
                        )
                )
                .Append(" | ")
                .Append(
                    result.Score is null
                        ? "—"
                        : string.Create(CultureInfo.InvariantCulture, $"{result.Score:0.0}")
                )
                .Append(" | ")
                .Append(result.Advisories.Count == 0 ? "—" : string.Join(", ", result.Advisories))
                .AppendLine(" |");
        }

        return builder.ToString();
    }

    private static string BuildSvg(BenchmarkRun run)
    {
        var categories = run
            .Benchmarks.Where(result => result.Score is not null)
            .GroupBy(result => result.Category, StringComparer.Ordinal)
            .Select(group => new
            {
                Name = group.Key,
                Score = Statistics.GeometricMean(group.Select(result => result.Score!.Value)),
            })
            .OrderBy(category => category.Name, StringComparer.Ordinal)
            .ToArray();
        var overall = Statistics.GeometricMean(categories.Select(category => category.Score));
        var height = 125 + (categories.Length * 48);
        var builder = new StringBuilder();
        builder
            .Append(
                $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"900\" height=\"{height}\" viewBox=\"0 0 900 {height}\">"
            )
            .Append(
                "<style>text{font-family:Segoe UI,Arial,sans-serif;fill:#e6edf3}.title{font-size:24px;font-weight:700}.label{font-size:15px}.value{font-size:14px;font-weight:700}</style>"
            )
            .Append("<rect width=\"900\" height=\"100%\" rx=\"14\" fill=\"#0d1117\"/>")
            .Append(
                $"<text x=\"28\" y=\"38\" class=\"title\">agent-windows performance · {overall:0.0}</text>"
            )
            .Append(
                $"<text x=\"28\" y=\"64\" class=\"label\">Baseline = 100 · lower is better · {Escape(run.CandidateSha[..Math.Min(8, run.CandidateSha.Length)])}</text>"
            );
        for (var index = 0; index < categories.Length; index++)
        {
            var category = categories[index];
            var y = 92 + (index * 48);
            var width = Math.Min(620, category.Score * 6.2);
            var color = category.Score <= 100 ? "#3fb950" : "#f85149";
            builder
                .Append(
                    $"<text x=\"28\" y=\"{y + 16}\" class=\"label\">{Escape(category.Name)}</text>"
                )
                .Append(
                    $"<rect x=\"190\" y=\"{y}\" width=\"620\" height=\"22\" rx=\"5\" fill=\"#21262d\"/>"
                )
                .Append(
                    $"<rect x=\"190\" y=\"{y}\" width=\"{width:0.0}\" height=\"22\" rx=\"5\" fill=\"{color}\"/>"
                )
                .Append(
                    $"<text x=\"825\" y=\"{y + 16}\" class=\"value\">{category.Score:0.0}</text>"
                );
        }

        return builder.Append("</svg>").ToString();
    }

    private static string Escape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}

namespace AgentWindows.Performance;

internal static class Statistics
{
    public static Measurement Calculate(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        var sorted = samples.Order().ToArray();
        var mean = samples.Average();
        var variance = samples.Sum(value => Math.Pow(value - mean, 2)) / samples.Count;
        return new Measurement
        {
            Median = Percentile(sorted, 0.5),
            P95 = Percentile(sorted, 0.95),
            Mean = mean,
            StandardDeviation = Math.Sqrt(variance),
            SampleCount = samples.Count,
            Samples = samples.ToArray(),
        };
    }

    public static double GeometricMean(IEnumerable<double> values)
    {
        var positive = values.Where(value => value > 0).ToArray();
        return positive.Length == 0 ? 100 : Math.Exp(positive.Average(value => Math.Log(value)));
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var position = (sorted.Count - 1) * percentile;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        return sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}

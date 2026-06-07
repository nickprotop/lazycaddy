// -----------------------------------------------------------------------
// LazyCaddy - pure parser for Caddy's Prometheus /metrics text.
// No HTTP, no UI: testable string-in / values-out functions.
// -----------------------------------------------------------------------

using System.Globalization;

namespace LazyCaddy.Services;

public static class MetricsParser
{
    /// <summary>Sum every caddy_http_requests_total sample (all label sets). 0 if absent.</summary>
    public static double SumRequestsTotal(string metricsText)
        => SumMetric(metricsText, "caddy_http_requests_total");

    /// <summary>Map of upstream address -> healthy, from caddy_reverse_proxy_upstreams_healthy.</summary>
    public static IReadOnlyDictionary<string, bool> HealthyUpstreams(string metricsText)
    {
        var result = new Dictionary<string, bool>();
        foreach (var line in EnumerateSamples(metricsText, "caddy_reverse_proxy_upstreams_healthy"))
        {
            var upstream = ExtractLabel(line.Labels, "upstream");
            if (upstream is not null)
                result[upstream] = line.Value != 0d;
        }
        return result;
    }

    /// <summary>req/s from two counter samples; 0 on counter reset or non-positive time.</summary>
    public static double RatePerSecond(double prev, double curr, double secondsElapsed)
    {
        if (secondsElapsed <= 0) return 0d;
        var delta = curr - prev;
        if (delta < 0) return 0d; // counter reset (Caddy restarted)
        return delta / secondsElapsed;
    }

    private static double SumMetric(string text, string name)
    {
        double sum = 0d;
        foreach (var s in EnumerateSamples(text, name))
            sum += s.Value;
        return sum;
    }

    private readonly record struct Sample(string Labels, double Value);

    private static IEnumerable<Sample> EnumerateSamples(string text, string metricName)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (!line.StartsWith(metricName, StringComparison.Ordinal)) continue;

            // Guard: a line equal to the metric name with nothing after is not a sample.
            if (line.Length == metricName.Length) continue;

            // Must be exactly the metric (next char is '{' or whitespace), not a prefix match.
            var after = line[metricName.Length];
            if (after != '{' && after != ' ' && after != '\t') continue;

            int sp = line.LastIndexOf(' ');
            if (sp < 0) continue;
            var valuePart = line[(sp + 1)..];
            if (!double.TryParse(valuePart, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                continue;

            string labels = "";
            int lb = line.IndexOf('{');
            int rb = line.IndexOf('}');
            if (lb >= 0 && rb > lb) labels = line[(lb + 1)..rb];

            yield return new Sample(labels, value);
        }
    }

    private static string? ExtractLabel(string labels, string key)
    {
        foreach (var part in labels.Split(','))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim() == key)
                return kv[1].Trim().Trim('"');
        }
        return null;
    }
}

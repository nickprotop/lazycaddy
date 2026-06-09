// -----------------------------------------------------------------------
// LazyCaddy - pure parser for Caddy's Prometheus /metrics text.
// No HTTP, no UI: testable string-in / values-out functions.
// -----------------------------------------------------------------------

using System.Globalization;

namespace LazyCaddy.Services;

/// <summary>Request counts by HTTP status class.</summary>
public readonly record struct StatusClassCounts(double C2xx, double C3xx, double C4xx, double C5xx)
{
    public double Total => C2xx + C3xx + C4xx + C5xx;
}

/// <summary>Request-latency percentiles in seconds (Unavailable when no histogram data).</summary>
public readonly record struct LatencyPercentiles(bool Available, double P50, double P95, double P99)
{
    public static LatencyPercentiles Unavailable => new(false, 0, 0, 0);
}

/// <summary>A label value and its summed metric count (e.g. handler → request count).</summary>
public readonly record struct LabelCount(string Label, double Count);

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

    /// <summary>Request counts bucketed by HTTP status class (2xx/3xx/4xx/5xx). The per-class code
    /// label lives on caddy_http_request_duration_seconds_count (caddy_http_requests_total carries
    /// no `code` label in current Caddy), so we read the histogram's count series. Codes that don't
    /// start 2–5 are ignored.</summary>
    public static StatusClassCounts StatusClasses(string metricsText)
    {
        double c2 = 0, c3 = 0, c4 = 0, c5 = 0;
        foreach (var s in EnumerateSamples(metricsText, "caddy_http_request_duration_seconds_count"))
        {
            var code = ExtractLabel(s.Labels, "code");
            if (code is null || code.Length == 0) continue;
            switch (code[0])
            {
                case '2': c2 += s.Value; break;
                case '3': c3 += s.Value; break;
                case '4': c4 += s.Value; break;
                case '5': c5 += s.Value; break;
            }
        }
        return new StatusClassCounts(c2, c3, c4, c5);
    }

    /// <summary>Sum of caddy_http_requests_in_flight across all servers (current concurrency).</summary>
    public static double InFlight(string metricsText)
        => SumMetric(metricsText, "caddy_http_requests_in_flight");

    /// <summary>p50/p95/p99 request latency (seconds) from the caddy_http_request_duration_seconds
    /// histogram, linearly interpolating within the target bucket. Unavailable if no buckets.</summary>
    public static LatencyPercentiles Percentiles(string metricsText)
    {
        // Aggregate cumulative bucket counts across all label sets: le -> summed count.
        var buckets = new SortedDictionary<double, double>();
        foreach (var s in EnumerateSamples(metricsText, "caddy_http_request_duration_seconds_bucket"))
        {
            var le = ExtractLabel(s.Labels, "le");
            if (le is null) continue;
            double bound = le is "+Inf" or "Inf"
                ? double.PositiveInfinity
                : (double.TryParse(le, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : double.NaN);
            if (double.IsNaN(bound)) continue;
            buckets[bound] = buckets.TryGetValue(bound, out var existing) ? existing + s.Value : s.Value;
        }
        if (buckets.Count == 0) return LatencyPercentiles.Unavailable;

        var total = buckets.Values.Count > 0 ? buckets[buckets.Keys.Last()] : 0d;
        if (total <= 0) return LatencyPercentiles.Unavailable;

        return new LatencyPercentiles(true,
            Quantile(buckets, total, 0.50),
            Quantile(buckets, total, 0.95),
            Quantile(buckets, total, 0.99));
    }

    // Prometheus-style histogram_quantile: find the bucket containing the rank, interpolate
    // linearly between its lower and upper bound. buckets is cumulative count keyed by upper "le".
    private static double Quantile(SortedDictionary<double, double> buckets, double total, double q)
    {
        var rank = q * total;
        double lowerBound = 0d, lowerCount = 0d;
        foreach (var (le, cum) in buckets)
        {
            if (cum >= rank)
            {
                if (double.IsPositiveInfinity(le)) return lowerBound; // open-ended top bucket: best estimate is its floor
                var span = cum - lowerCount;
                if (span <= 0) return le;
                var frac = (rank - lowerCount) / span;
                return lowerBound + frac * (le - lowerBound);
            }
            lowerBound = double.IsPositiveInfinity(le) ? lowerBound : le;
            lowerCount = cum;
        }
        return lowerBound;
    }

    /// <summary>Top N label values of <paramref name="metricName"/> ranked by summed sample value
    /// (e.g. busiest handlers by request count). Descending; ties broken by label name.</summary>
    public static IReadOnlyList<LabelCount> TopByLabel(string metricsText, string metricName, string label, int n)
    {
        var totals = new Dictionary<string, double>();
        foreach (var s in EnumerateSamples(metricsText, metricName))
        {
            var v = ExtractLabel(s.Labels, label);
            if (v is null || v.Length == 0) continue;
            totals[v] = totals.TryGetValue(v, out var existing) ? existing + s.Value : s.Value;
        }
        return totals
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(Math.Max(0, n))
            .Select(kv => new LabelCount(kv.Key, kv.Value))
            .ToList();
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

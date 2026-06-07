// -----------------------------------------------------------------------
// LazyCaddy - pure parser for GET /reverse_proxy/upstreams.
// -----------------------------------------------------------------------

using System.Text.Json;
using LazyCaddy.Models;

namespace LazyCaddy.Services;

public static class UpstreamsParser
{
    /// <summary>Parse the upstreams array into DTOs (reachability filled later by the prober).</summary>
    public static IReadOnlyList<Upstream> Parse(string json)
    {
        var result = new List<Upstream>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (!el.TryGetProperty("address", out var addr) || addr.ValueKind != JsonValueKind.String)
                continue;
            result.Add(new Upstream(
                Address: addr.GetString()!,
                Reachability: UpstreamReachability.Unknown,
                Latency: null,
                UsedByRoutes: Array.Empty<string>()));
        }
        return result;
    }
}

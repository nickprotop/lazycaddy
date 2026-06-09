// -----------------------------------------------------------------------
// LazyCaddy - concrete ICaddyAdmin over HttpClient.
//
// Wires the real Caddy admin API into the DTOs via the pure parsers
// (ConfigParser, UpstreamsParser, MetricsParser).
// -----------------------------------------------------------------------

using System.Linq;
using System.Net.Http;
using LazyCaddy.Configuration;
using LazyCaddy.Models;

namespace LazyCaddy.Services;

public sealed class CaddyAdminClient : ICaddyAdmin, IDisposable
{
    private readonly HttpClient _http;

    /// <summary>
    /// When true, every call throws to exercise the status bar's red/disconnected
    /// path. Flip via the constructor for manual testing; real wiring leaves it false.
    /// </summary>
    private readonly bool _simulateDisconnected;

    // Previous /metrics sample, for computing request-rate deltas between polls.
    private double? _prevRequestsTotal;
    private DateTimeOffset _prevMetricsAt;
    private readonly List<double> _rateHistory = new();
    private const int RateHistoryMax = 50;

    public CaddyAdminClient(LazyCaddyConfig config, bool simulateDisconnected = false)
        : this(config, null, simulateDisconnected) { }

    /// <summary>Test seam: inject a custom <see cref="HttpMessageHandler"/> (e.g. a stub).</summary>
    internal CaddyAdminClient(LazyCaddyConfig config, HttpMessageHandler? handler, bool simulateDisconnected = false)
    {
        _http = (handler is null ? new HttpClient() : new HttpClient(handler));
        _http.BaseAddress = new Uri(config.AdminApiUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromMilliseconds(config.HttpTimeoutMs);
        _simulateDisconnected = simulateDisconnected;
    }

    private void ThrowIfSimulatingDisconnect()
    {
        if (_simulateDisconnected)
            throw new HttpRequestException("Simulated admin API disconnect (CaddyAdminClient._simulateDisconnected).");
    }

    /// <summary>Sends a GET request to the admin API and returns the response body string.</summary>
    private async Task<string> GetStringAsync(string relativePath, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(relativePath, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    // ── ICaddyAdmin ──────────────────────────────────────────────────────

    public async Task<CaddyStatus> GetStatusAsync(CancellationToken ct = default)
    {
        ThrowIfSimulatingDisconnect();
        using var resp = await _http.GetAsync("config/", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        var version = resp.Headers.TryGetValues("Server", out var vs)
            ? string.Join(" ", vs) : "unknown";

        var routes = ConfigParser.ParseRoutes(json);
        var certs = ConfigParser.ParseCerts(json);
        var now = DateTimeOffset.Now;

        return new CaddyStatus(
            Running: true,
            Version: version,
            Uptime: TimeSpan.Zero, // not exposed by the admin API; left zero for now
            RouteCount: routes.Count,
            CertValidCount: certs.Count(c => c.DaysLeft(now) >= 30),
            CertExpiringCount: certs.Count(c => c.DaysLeft(now) < 30),
            UpstreamUpCount: 0,
            UpstreamDownCount: 0);
    }

    public async Task<IReadOnlyList<Route>> GetRoutesAsync(CancellationToken ct = default)
    {
        ThrowIfSimulatingDisconnect();
        var json = await GetStringAsync("config/", ct).ConfigureAwait(false);
        return ConfigParser.ParseRoutes(json);
    }

    public async Task<IReadOnlyList<Cert>> GetCertsAsync(CancellationToken ct = default)
    {
        ThrowIfSimulatingDisconnect();
        var json = await GetStringAsync("config/", ct).ConfigureAwait(false);
        return ConfigParser.ParseCerts(json);
    }

    public async Task<IReadOnlyList<Upstream>> GetUpstreamsAsync(CancellationToken ct = default)
    {
        ThrowIfSimulatingDisconnect();
        var json = await GetStringAsync("reverse_proxy/upstreams", ct).ConfigureAwait(false);
        var upstreams = UpstreamsParser.Parse(json);

        // Annotate UsedByRoutes from the running config.
        var routes = ConfigParser.ParseRoutes(
            await GetStringAsync("config/", ct).ConfigureAwait(false));
        var byUpstream = new Dictionary<string, List<string>>();
        foreach (var r in routes)
            foreach (var dial in r.Upstream.Split(", ", StringSplitOptions.RemoveEmptyEntries))
                (byUpstream.TryGetValue(dial, out var l) ? l : byUpstream[dial] = new()).Add(r.HostOrMatch);

        return upstreams
            .Select(u => byUpstream.TryGetValue(u.Address, out var used)
                ? u with { UsedByRoutes = used }
                : u)
            .ToList();
    }

    public async Task<MetricsSnapshot> GetMetricsAsync(CancellationToken ct = default)
    {
        ThrowIfSimulatingDisconnect();
        string text;
        try
        {
            text = await GetStringAsync("metrics", ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return MetricsSnapshot.Unavailable; // /metrics not enabled
        }

        var total = MetricsParser.SumRequestsTotal(text);
        var now = DateTimeOffset.Now;
        if (_prevRequestsTotal is { } prev)
        {
            var rate = MetricsParser.RatePerSecond(prev, total, (now - _prevMetricsAt).TotalSeconds);
            _rateHistory.Add(rate);
            while (_rateHistory.Count > RateHistoryMax) _rateHistory.RemoveAt(0);
        }
        _prevRequestsTotal = total;
        _prevMetricsAt = now;

        return new MetricsSnapshot(
            Available: true,
            RequestRate: _rateHistory.ToArray(),
            StatusClasses: MetricsParser.StatusClasses(text),
            InFlight: MetricsParser.InFlight(text),
            Latency: MetricsParser.Percentiles(text),
            TopHandlers: MetricsParser.TopByLabel(text, "caddy_http_requests_total", "handler", 6));
    }

    public async Task<string> GetRawConfigAsync(CancellationToken ct = default)
    {
        ThrowIfSimulatingDisconnect();
        var json = await GetStringAsync("config/", ct).ConfigureAwait(false);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> GetConfigNodeAsync(string path, CancellationToken ct = default)
    {
        ThrowIfSimulatingDisconnect();
        return await GetStringAsync($"config/{path}", ct).ConfigureAwait(false);
    }

    public Task<WriteResult> PatchConfigAsync(string path, string json, CancellationToken ct = default)
        => SendWriteAsync(HttpMethod.Patch, $"config/{path}", json, ct);

    public Task<WriteResult> PutConfigAsync(string path, string json, CancellationToken ct = default)
        => SendWriteAsync(HttpMethod.Put, $"config/{path}", json, ct);

    public async Task<WriteResult> UpsertConfigAsync(string path, string json, CancellationToken ct = default)
    {
        if (_simulateDisconnected) return WriteResult.Fail("Simulated disconnect.");
        // PATCH replaces an existing node but 404s when the node is absent.
        var (patch, patchStatus) = await SendWriteStatusAsync(HttpMethod.Patch, $"config/{path}", json, ct).ConfigureAwait(false);
        if (patch.Success || patchStatus != System.Net.HttpStatusCode.NotFound)
            return patch;
        // Node was absent → PUT creates it.
        return await SendWriteAsync(HttpMethod.Put, $"config/{path}", json, ct).ConfigureAwait(false);
    }

    public Task<WriteResult> PostConfigAsync(string path, string json, CancellationToken ct = default)
        => SendWriteAsync(HttpMethod.Post, $"config/{path}", json, ct);

    public Task<WriteResult> DeleteConfigAsync(string path, CancellationToken ct = default)
        => SendWriteAsync(HttpMethod.Delete, $"config/{path}", null, ct);

    public Task<WriteResult> LoadConfigAsync(string fullConfigJson, CancellationToken ct = default)
        => SendWriteAsync(HttpMethod.Post, "load", fullConfigJson, ct);

    private async Task<WriteResult> SendWriteAsync(HttpMethod method, string relPath, string? json, CancellationToken ct)
        => (await SendWriteStatusAsync(method, relPath, json, ct).ConfigureAwait(false)).Result;

    /// <summary>As <see cref="SendWriteAsync"/> but also returns the HTTP status (for upsert fallback logic).</summary>
    private async Task<(WriteResult Result, System.Net.HttpStatusCode Status)> SendWriteStatusAsync(
        HttpMethod method, string relPath, string? json, CancellationToken ct)
    {
        if (_simulateDisconnected)
            return (WriteResult.Fail("Simulated disconnect."), 0);
        try
        {
            using var req = new HttpRequestMessage(method, relPath);
            if (json is not null)
                req.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return (WriteResult.Ok, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (WriteResult.Fail(string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)resp.StatusCode}" : body), resp.StatusCode);
        }
        catch (Exception ex)
        {
            return (WriteResult.Fail(ex.Message), 0);
        }
    }

    public void Dispose() => _http.Dispose();
}

using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class MetricsParserTests
{
    [Fact]
    public void SumRequestsTotal_AddsAllRequestCounters()
    {
        var text = """
            # HELP caddy_http_requests_total ...
            caddy_http_requests_total{handler="reverse_proxy",code="200"} 40
            caddy_http_requests_total{handler="reverse_proxy",code="404"} 20
            promhttp_metric_handler_requests_total{code="200"} 999
            """;
        Assert.Equal(60d, MetricsParser.SumRequestsTotal(text));
    }

    [Fact]
    public void SumRequestsTotal_ReturnsZero_WhenMetricAbsent()
    {
        var text = "caddy_admin_http_requests_total{path=\"/config/\"} 4\n";
        Assert.Equal(0d, MetricsParser.SumRequestsTotal(text));
    }

    [Fact]
    public void HealthyUpstreams_ParsesAddressesMarkedHealthy()
    {
        var text = """
            caddy_reverse_proxy_upstreams_healthy{upstream="127.0.0.1:8090"} 1
            caddy_reverse_proxy_upstreams_healthy{upstream="10.0.0.5:8080"} 0
            """;
        var map = MetricsParser.HealthyUpstreams(text);
        Assert.True(map["127.0.0.1:8090"]);
        Assert.False(map["10.0.0.5:8080"]);
    }

    [Fact]
    public void RatePerSecond_ComputesDeltaOverTime()
    {
        var rate = MetricsParser.RatePerSecond(prev: 100, curr: 160, secondsElapsed: 5);
        Assert.Equal(12d, rate);
    }

    [Fact]
    public void RatePerSecond_ReturnsZero_OnCounterResetOrZeroTime()
    {
        Assert.Equal(0d, MetricsParser.RatePerSecond(prev: 200, curr: 10, secondsElapsed: 5));
        Assert.Equal(0d, MetricsParser.RatePerSecond(prev: 10, curr: 20, secondsElapsed: 0));
    }

    // --- Status-class breakdown ---------------------------------------------------

    [Fact]
    public void StatusClasses_BucketsByLeadingDigit()
    {
        // The code label lives on the duration histogram's _count series (Caddy's
        // caddy_http_requests_total has no `code` label), so StatusClasses reads that.
        var text = """
            caddy_http_request_duration_seconds_count{handler="reverse_proxy",code="200"} 80
            caddy_http_request_duration_seconds_count{handler="reverse_proxy",code="204"} 5
            caddy_http_request_duration_seconds_count{handler="reverse_proxy",code="301"} 10
            caddy_http_request_duration_seconds_count{handler="reverse_proxy",code="404"} 8
            caddy_http_request_duration_seconds_count{handler="reverse_proxy",code="500"} 3
            promhttp_metric_handler_requests_total{code="200"} 999
            """;
        var c = MetricsParser.StatusClasses(text);
        Assert.Equal(85d, c.C2xx);
        Assert.Equal(10d, c.C3xx);
        Assert.Equal(8d, c.C4xx);
        Assert.Equal(3d, c.C5xx);
        Assert.Equal(106d, c.Total);
    }

    [Fact]
    public void StatusClasses_EmptyWhenAbsent()
    {
        var c = MetricsParser.StatusClasses("# nothing here\n");
        Assert.Equal(0d, c.Total);
    }

    // --- In-flight gauge ----------------------------------------------------------

    [Fact]
    public void InFlight_SumsGauge()
    {
        var text = """
            caddy_http_requests_in_flight{server="srv0"} 4
            caddy_http_requests_in_flight{server="srv1"} 3
            """;
        Assert.Equal(7d, MetricsParser.InFlight(text));
    }

    // --- Latency percentiles from histogram buckets -------------------------------

    [Fact]
    public void Percentiles_InterpolateWithinBucket()
    {
        // 100 observations spread across buckets (cumulative "le" counts):
        //   ≤0.005:10  ≤0.010:30  ≤0.025:70  ≤0.05:90  ≤0.1:98  ≤0.25:100  +Inf:100
        var text = """
            caddy_http_request_duration_seconds_bucket{le="0.005"} 10
            caddy_http_request_duration_seconds_bucket{le="0.01"} 30
            caddy_http_request_duration_seconds_bucket{le="0.025"} 70
            caddy_http_request_duration_seconds_bucket{le="0.05"} 90
            caddy_http_request_duration_seconds_bucket{le="0.1"} 98
            caddy_http_request_duration_seconds_bucket{le="0.25"} 100
            caddy_http_request_duration_seconds_bucket{le="+Inf"} 100
            caddy_http_request_duration_seconds_sum 2.5
            caddy_http_request_duration_seconds_count 100
            """;
        var p = MetricsParser.Percentiles(text);
        // p50 → 50th obs falls in (0.01, 0.025] bucket → between 10ms and 25ms.
        Assert.InRange(p.P50, 0.010, 0.025);
        // p95 → in (0.05, 0.1] → between 50ms and 100ms.
        Assert.InRange(p.P95, 0.05, 0.1);
        // p99 → in (0.05, 0.1] as well (98 ≤ 99 ≤ 100 spans 0.1..0.25, actually 99 is between 98 and 100).
        Assert.InRange(p.P99, 0.1, 0.25);
        Assert.True(p.Available);
    }

    [Fact]
    public void Percentiles_UnavailableWhenNoBuckets()
        => Assert.False(MetricsParser.Percentiles("# none\n").Available);

    // --- Top-by-label -------------------------------------------------------------

    [Fact]
    public void TopByLabel_RanksAndCaps()
    {
        var text = """
            caddy_http_requests_total{handler="reverse_proxy",code="200"} 100
            caddy_http_requests_total{handler="reverse_proxy",code="404"} 20
            caddy_http_requests_total{handler="file_server",code="200"} 50
            caddy_http_requests_total{handler="static_response",code="200"} 5
            """;
        var top = MetricsParser.TopByLabel(text, "caddy_http_requests_total", "handler", 2);
        Assert.Equal(2, top.Count);
        Assert.Equal("reverse_proxy", top[0].Label);
        Assert.Equal(120d, top[0].Count);
        Assert.Equal("file_server", top[1].Label);
        Assert.Equal(50d, top[1].Count);
    }
}

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
}

using System.Text.Json;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class HandlerPatchD1Tests
{
    [Fact]
    public void LoadBalancing_SimplePolicy_NoParams()
    {
        var json = HandlerPatch.LoadBalancing(policy: "round_robin", policyParam: "",
            retries: 3, tryDuration: "5s", tryInterval: "");
        using var d = JsonDocument.Parse(json);
        Assert.Equal("round_robin", d.RootElement.GetProperty("selection_policy").GetProperty("policy").GetString());
        Assert.Equal(3, d.RootElement.GetProperty("retries").GetInt32());
        Assert.Equal("5s", d.RootElement.GetProperty("try_duration").GetString());
        Assert.False(d.RootElement.TryGetProperty("try_interval", out _));
    }

    [Fact]
    public void LoadBalancing_HeaderPolicy_AddsFieldParam()
    {
        var json = HandlerPatch.LoadBalancing("header", "X-User", 0, "", "");
        using var d = JsonDocument.Parse(json);
        var sp = d.RootElement.GetProperty("selection_policy");
        Assert.Equal("header", sp.GetProperty("policy").GetString());
        Assert.Equal("X-User", sp.GetProperty("field").GetString());
        Assert.False(d.RootElement.TryGetProperty("retries", out _)); // 0 omitted
    }

    [Fact]
    public void LoadBalancing_NoPolicy_OmitsSelectionPolicy()
    {
        var json = HandlerPatch.LoadBalancing("", "", 2, "", "");
        using var d = JsonDocument.Parse(json);
        Assert.False(d.RootElement.TryGetProperty("selection_policy", out _));
        Assert.Equal(2, d.RootElement.GetProperty("retries").GetInt32());
    }

    [Fact]
    public void HealthChecks_ActiveAndPassive()
    {
        var active = new ActiveHealthCheckInput(Uri: "/health", Port: 8080, Method: "GET",
            Interval: "10s", Timeout: "5s", Passes: 1, Fails: 2, ExpectStatus: 200, ExpectBody: "");
        var passive = new PassiveHealthCheckInput(FailDuration: "30s", MaxFails: 3,
            UnhealthyRequestCount: 0, UnhealthyStatus: new[] { 500, 502 }, UnhealthyLatency: "2s");
        var json = HandlerPatch.HealthChecks(active, passive);
        using var d = JsonDocument.Parse(json);
        Assert.Equal("/health", d.RootElement.GetProperty("active").GetProperty("uri").GetString());
        Assert.Equal(8080, d.RootElement.GetProperty("active").GetProperty("port").GetInt32());
        Assert.Equal(200, d.RootElement.GetProperty("active").GetProperty("expect_status").GetInt32());
        Assert.Equal("30s", d.RootElement.GetProperty("passive").GetProperty("fail_duration").GetString());
        Assert.Equal(502, d.RootElement.GetProperty("passive").GetProperty("unhealthy_status")[1].GetInt32());
    }

    [Fact]
    public void HealthChecks_EmptyActive_Omitted()
    {
        var active = new ActiveHealthCheckInput("", 0, "", "", "", 0, 0, 0, "");
        var passive = new PassiveHealthCheckInput("", 0, 0, System.Array.Empty<int>(), "");
        var json = HandlerPatch.HealthChecks(active, passive);
        using var d = JsonDocument.Parse(json);
        Assert.False(d.RootElement.TryGetProperty("active", out _));
        Assert.False(d.RootElement.TryGetProperty("passive", out _));
    }
}

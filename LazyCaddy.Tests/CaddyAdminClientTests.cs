using System.Net;
using LazyCaddy.Configuration;
using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class CaddyAdminClientTests
{
    // Caddy has no single create-or-replace verb for a config node:
    //   node exists  -> PATCH 200, PUT 409
    //   node absent  -> PATCH 404, PUT 200
    // UpsertConfigAsync must paper over this by trying PATCH then falling back to PUT on 404.

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<(HttpMethod Method, string Path)> Calls { get; } = new();
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath));
            return Task.FromResult(_respond(request));
        }
    }

    private static CaddyAdminClient Client(StubHandler handler) =>
        new(new LazyCaddyConfig { AdminApiUrl = "http://localhost:9999" }, handler);

    [Fact]
    public async Task Upsert_ExistingNode_UsesPatch_DoesNotPut()
    {
        var handler = new StubHandler(req => new HttpResponseMessage(
            req.Method == HttpMethod.Patch ? HttpStatusCode.OK : HttpStatusCode.Conflict));
        var client = Client(handler);

        var result = await client.UpsertConfigAsync("apps/http/x/health_checks", "{}");

        Assert.True(result.Success);
        Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Patch, handler.Calls[0].Method);
    }

    [Fact]
    public async Task Upsert_AbsentNode_PatchFallsBackToPut()
    {
        var handler = new StubHandler(req => new HttpResponseMessage(
            req.Method == HttpMethod.Patch ? HttpStatusCode.NotFound : HttpStatusCode.OK));
        var client = Client(handler);

        var result = await client.UpsertConfigAsync("apps/http/x/health_checks", "{}");

        Assert.True(result.Success);
        Assert.Equal(2, handler.Calls.Count);
        Assert.Equal(HttpMethod.Patch, handler.Calls[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Calls[1].Method);
    }

    [Fact]
    public async Task Upsert_PatchFailsNon404_ReturnsError_NoPut()
    {
        // A 400 (bad config) is a real error, not "node absent" — must NOT fall back to PUT.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid duration")
        });
        var client = Client(handler);

        var result = await client.UpsertConfigAsync("apps/http/x/health_checks", "{}");

        Assert.False(result.Success);
        Assert.Contains("invalid duration", result.Error);
        Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Patch, handler.Calls[0].Method);
    }
}

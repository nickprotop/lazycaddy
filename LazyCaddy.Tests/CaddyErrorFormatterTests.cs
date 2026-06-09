using LazyCaddy.Services;
using Xunit;

namespace LazyCaddy.Tests;

public class CaddyErrorFormatterTests
{
    [Fact]
    public void UnknownModule_BecomesFriendlyHandlerMessage()
    {
        var raw = "loading config: loading new config: loading http app module: provision http: server srv0: setting up route handlers: route 0: loading handler modules: position 0: loading module 'this_handler_does_not_exist': unknown module: http.handlers.this_handler_does_not_exist";
        var msg = CaddyErrorFormatter.Format(raw);
        Assert.Contains("Unknown handler", msg);
        Assert.Contains("this_handler_does_not_exist", msg);
        // The Go boilerplate prefix chain is stripped.
        Assert.DoesNotContain("loading config: loading new config", msg);
    }

    [Fact]
    public void RoutePrefix_IsPreserved()
    {
        var raw = "provision http: server srv0: setting up route handlers: route 2: loading handler modules: position 0: loading module 'foo': unknown module: http.handlers.foo";
        var msg = CaddyErrorFormatter.Format(raw);
        Assert.Contains("Route 2", msg);
    }

    [Fact]
    public void JsonError_WrappedInErrorObject_IsUnwrapped()
    {
        // Caddy admin returns {"error":"..."}; the formatter accepts the raw body and unwraps it.
        var body = """{"error":"loading config: loading new config: provision http: server srv0: setting up route handlers: route 0: loading handler modules: position 0: loading module 'bogus': unknown module: http.handlers.bogus"}""";
        var msg = CaddyErrorFormatter.Format(body);
        Assert.Contains("Unknown handler", msg);
        Assert.Contains("bogus", msg);
    }

    [Fact]
    public void TypeMismatch_BecomesFriendly()
    {
        var raw = "json: cannot unmarshal string into Go struct field StaticResponse.status_code of type int";
        var msg = CaddyErrorFormatter.Format(raw);
        Assert.Contains("number", msg, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownPattern_FallsBackToTrimmedRaw()
    {
        var raw = "some totally unexpected error text from a future caddy version";
        var msg = CaddyErrorFormatter.Format(raw);
        Assert.Contains("future caddy version", msg);
    }

    [Fact]
    public void Empty_GivesGenericMessage()
    {
        Assert.False(string.IsNullOrWhiteSpace(CaddyErrorFormatter.Format("")));
        Assert.False(string.IsNullOrWhiteSpace(CaddyErrorFormatter.Format(null)));
    }

    [Fact]
    public void LongRaw_IsTruncated()
    {
        var raw = new string('x', 500);
        var msg = CaddyErrorFormatter.Format(raw);
        Assert.True(msg.Length <= 200, $"expected truncation, got {msg.Length} chars");
    }
}

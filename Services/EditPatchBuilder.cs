// -----------------------------------------------------------------------
// LazyCaddy - pure builders that turn dialog form values into the JSON
// fragments Caddy expects at specific config paths.
// -----------------------------------------------------------------------

using System.Text.Json;

namespace LazyCaddy.Services;

public static class EditPatchBuilder
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    /// <summary>JSON for a reverse_proxy upstreams array: [{"dial":"..."}].</summary>
    public static string UpstreamsArray(IEnumerable<string> dials)
        => JsonSerializer.Serialize(dials.Select(d => new { dial = d }), Opt);

    /// <summary>JSON for a route match array with a single host matcher: [{"host":[...]}].</summary>
    public static string HostMatcher(IEnumerable<string> hosts)
        => JsonSerializer.Serialize(new object[] { new { host = hosts.ToArray() } }, Opt);

    /// <summary>JSON for a complete reverse-proxy route (host -> upstream).</summary>
    public static string ReverseProxyRoute(string host, string upstreamDial)
        => JsonSerializer.Serialize(new
        {
            match = new object[] { new { host = new[] { host } } },
            handle = new object[]
            {
                new { handler = "reverse_proxy", upstreams = new object[] { new { dial = upstreamDial } } }
            },
            terminal = true
        }, Opt);
}
